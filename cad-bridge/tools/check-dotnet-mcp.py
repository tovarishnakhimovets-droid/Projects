"""Manual upstream MCP check; live modes explicitly enable scratch-drawing edits."""

import argparse
import json
import ntpath
from pathlib import Path
import sys
import traceback
import uuid

import anyio
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / "dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll"


async def call(session, name, arguments=None, *, expect_error=False):
    with anyio.fail_after(40):
        response = await session.call_tool(name, arguments or {})
    text = "\n".join(block.text for block in response.content if block.type == "text")
    if response.isError:
        raise RuntimeError(f"{name}: MCP error: {text}")
    try:
        payload = json.loads(text)
    except json.JSONDecodeError as error:
        raise RuntimeError(f"{name}: invalid JSON response: {text}") from error
    if expect_error:
        if payload.get("ok"):
            raise RuntimeError(f"{name}: expected refusal, received {payload}")
        return payload
    if not payload.get("ok"):
        raise RuntimeError(f"{name}: {payload}")
    return payload["result"]


async def check_variable(session, document):
    variables = await call(session, "dwg_get_variables")
    original = variables["variables"]["DIMSCALE"]
    print("ORIGINAL DIMSCALE:", original, flush=True)
    try:
        await call(session, "dwg_set_system_variable", {"name": "DIMSCALE", "value": "2.5"})
        actual = await call(session, "dwg_get_variables")
        if actual["variables"]["DIMSCALE"] != 2.5:
            raise RuntimeError(f"Expected DIMSCALE=2.5, received {actual}")
        print("SET: string 2.5 -> number 2.5")
        refused = await call(session, "dwg_set_system_variable",
                             {"name": "DIMSCALE", "value": "2,5"}, expect_error=True)
        if "cannot be coerced" not in (refused.get("error") or ""):
            raise RuntimeError(f"Unexpected refusal: {refused}")
        actual = await call(session, "dwg_get_variables")
        if actual["variables"]["DIMSCALE"] != 2.5:
            raise RuntimeError(f"Rejected value changed DIMSCALE: {actual}")
        print("REFUSED: string 2,5; DIMSCALE unchanged")
    finally:
        current = await call(session, "dwg_get_drawing_info")
        if current["document_name"] != document:
            raise RuntimeError(f"Drawing changed; restore DIMSCALE={original} in {document}")
        actual = await call(session, "dwg_get_variables")
        if actual["variables"]["DIMSCALE"] != original:
            await call(session, "dwg_set_system_variable", {"name": "DIMSCALE", "value": original})
        restored = await call(session, "dwg_get_variables")
        if restored["variables"]["DIMSCALE"] != original:
            raise RuntimeError(f"DIMSCALE restoration failed: {restored}")
        print("RESTORED DIMSCALE:", original, flush=True)
    print("PASS: invariant numeric strings / rejected comma / restored variable")


async def check_documents(session, parameters, paths):
    """Exercise session routing against two explicitly named open DWG copies."""
    normalize = lambda path: ntpath.normcase(ntpath.normpath(path))
    requested = {normalize(path) for path in paths}
    initial = await call(session, "dwg_get_drawing_info")
    original_selection = await call(session, "dwg_get_selection")
    capture = await call(session, "dwg_capture_view_image")
    listing = await call(session, "dwg_list_documents")
    active_id = listing["active_document_id"]
    documents = {normalize(doc["document_path"]): doc for doc in listing["documents"]
                 if doc.get("document_path") and normalize(doc["document_path"]) in requested}
    if len(requested) != 2 or set(documents) != requested:
        raise RuntimeError(f"Expected both exact open DWG copies, received: {listing}")
    inactive = next((doc for doc in documents.values() if not doc["is_active"]), None)
    if inactive is None or normalize(initial["document_path"]) not in requested:
        raise RuntimeError("Keep one of the two named DWGs active before this check")

    try:
        for doc in documents.values():
            await call(session, "dwg_use_document", {"document_id": doc["document_id"]})
            actual = await call(session, "dwg_get_drawing_info")
            if normalize(actual["document_path"]) != normalize(doc["document_path"]):
                raise RuntimeError(f"Wrong routed drawing: {actual}")
            if actual["fingerprint"] != doc["fingerprint"]:
                raise RuntimeError("Routed drawing fingerprint differs")
            found = await call(session, "dwg_query_entities", {"limit": 3, "include_geometry": False})
            await call(session, "dwg_count_entities")
            await call(session, "dwg_list_text_styles", {"limit": 2})
            if found["entities"]:
                await call(session, "dwg_get_entity_properties", {
                    "handles": json.dumps([found["entities"][0]["handle"]]), "includeGeometry": False})
            print("PASS: routed database readers:", doc["document_path"], flush=True)

        await call(session, "dwg_use_document", {"document_id": inactive["document_id"]})
        for name, arguments in (
                ("dwg_get_selection", {}),
                ("dwg_get_variables", {}),
                ("dwg_create_line", {"start": '{"x":0,"y":0}', "end": '{"x":1,"y":1}'})):
            refused = await call(session, name, arguments, expect_error=True)
            if "requires the selected drawing to be the active" not in (refused.get("error") or ""):
                raise RuntimeError(f"Unexpected inactive refusal: {refused}")
        print("PASS: inactive selection/variables/write requests refused before execution", flush=True)

        refused = await call(session, "dwg_use_document", {"document_id": uuid.uuid4().hex}, expect_error=True)
        if "no longer open" not in (refused.get("error") or ""):
            raise RuntimeError(f"Unexpected missing-document refusal: {refused}")
        actual = await call(session, "dwg_get_drawing_info")
        if normalize(actual["document_path"]) != normalize(inactive["document_path"]):
            raise RuntimeError("Failed selection changed the existing session target")

        # A second stdio process must still follow the UI, despite the first pin.
        async with stdio_client(parameters) as (read, write):
            async with ClientSession(read, write) as second:
                with anyio.fail_after(15):
                    await second.initialize()
                other = await call(second, "dwg_get_drawing_info")
                if normalize(other["document_path"]) != normalize(initial["document_path"]):
                    raise RuntimeError("Document selection leaked into a second MCP process")
        print("PASS: session isolation and failed-selection preservation", flush=True)
    finally:
        await call(session, "dwg_use_document", {"document_id": None})

    final = await call(session, "dwg_get_drawing_info")
    final_selection = await call(session, "dwg_get_selection")
    final_capture = await call(session, "dwg_capture_view_image")
    final_list = await call(session, "dwg_list_documents")
    if final_list["active_document_id"] != active_id or final["document_path"] != initial["document_path"]:
        raise RuntimeError("The active AutoCAD drawing changed during routing")
    if final["dbmod"] != initial["dbmod"]:
        raise RuntimeError("The active drawing DBMOD changed during database reads")
    if final_selection != original_selection or final_capture["camera"] != capture["camera"]:
        raise RuntimeError("The UI selection or camera changed during routing")
    print("PASS: active tab, selection, camera and DBMOD unchanged; follow-active restored", flush=True)


async def check(live_line, live_variable, inspect_variable, list_tools, server, target, dotnet,
                live_documents=False, document_paths=None):
    if not server.is_file():
        raise FileNotFoundError(server)
    # Listing the write tool's schema requires registering it, but invokes no CAD handler.
    writable = live_line or live_variable or inspect_variable or list_tools or live_documents
    toolsets = ("query,modify,drawing,view" if list_tools or live_documents else
                "query,drawing" if live_variable or inspect_variable else
                "query,modify" if live_line else "query")
    parameters = StdioServerParameters(
        command=dotnet,
        args=[str(server), "--target", str(target), "--toolsets", toolsets],
        env={"BIMWRIGHT_DWG_READ_ONLY": "false" if writable else "true",
             "Logging__LogLevel__Default": "Warning"},
    )
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            with anyio.fail_after(15):
                initialized = await session.initialize()
            print(f"MCP: {initialized.serverInfo.name} {initialized.serverInfo.version}")
            if live_documents:
                await check_documents(session, parameters, document_paths)
                return
            if inspect_variable or list_tools:
                with anyio.fail_after(15):
                    listing = await session.list_tools()
                if list_tools:
                    print(f"TOOLS: {len(listing.tools)}")
                    for tool in sorted(listing.tools, key=lambda tool: tool.name):
                        print(tool.name)
                tool = next((tool for tool in listing.tools
                             if tool.name == "dwg_set_system_variable"), None)
                if tool is None:
                    raise RuntimeError("dwg_set_system_variable is not registered")
                print("INPUT SCHEMA:", json.dumps(tool.inputSchema, ensure_ascii=False))
                print("PASS: schema inspected; no CAD tool was called")
                return
            drawing = await call(session, "dwg_get_drawing_info")
            print("DRAWING:", json.dumps(drawing, ensure_ascii=False))
            if live_variable:
                await check_variable(session, drawing["document_name"])
                return
            if not live_line:
                print("PASS: read-only connection")
                return

            document = drawing["document_name"]
            created = await call(session, "dwg_create_line", {
                "start": json.dumps({"x": 0, "y": 0, "z": 0}),
                "end": json.dumps({"x": 100, "y": 0, "z": 0}),
                "layer": "0", "color_index": 1,
            })
            handle = created["handle"]
            print("CREATED:", handle, flush=True)
            handles = {"handles": json.dumps([handle])}
            try:
                readback = await call(session, "dwg_get_entity_properties", {
                    **handles, "includeGeometry": True,
                })
                item = readback["entities"][0]
                if not item["ok"]:
                    raise RuntimeError(f"Readback failed: {item}")
                entity = item["entity"]
                if entity["type"] != "Line" or abs(entity["length"] - 100) > 1e-8:
                    raise RuntimeError(f"Unexpected geometry: {entity}")
                print("READ:", json.dumps(entity, ensure_ascii=False))
            finally:
                # Handles belong to one database. Refuse cleanup after a tab switch.
                current = await call(session, "dwg_get_drawing_info")
                if current["document_name"] != document:
                    raise RuntimeError(
                        f"Drawing changed. Test line {handle} remains in {document}; "
                        "no erase was sent."
                    )
                erased = await call(session, "dwg_erase_entities", handles)
                if not erased["results"][0]["ok"]:
                    raise RuntimeError(f"Cleanup failed for {handle}: {erased}")
                print("ERASED:", handle, flush=True)

            remaining = await call(session, "dwg_get_entity_properties", handles)
            if remaining["entities"][0]["ok"]:
                raise RuntimeError(f"Line {handle} remains available after erase")
            print("PASS: create / read / erase")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", type=Path, default=SERVER, help="Override the locally built server DLL")
    parser.add_argument("--target", type=int, choices=(2026, 2027), default=2027, help="AutoCAD target year")
    parser.add_argument("--dotnet", default="dotnet", help="dotnet executable (default: PATH)")
    live = parser.add_mutually_exclusive_group()
    live.add_argument("--live-line", action="store_true",
                      help="Create and erase one line in the active scratch drawing")
    live.add_argument("--live-variable", action="store_true",
                      help="Check decimal strings using DIMSCALE and restore its original value")
    live.add_argument("--inspect-variable", action="store_true",
                      help="Print the variable setter's input schema without calling any CAD tool")
    live.add_argument("--list-tools", action="store_true",
                      help="List the selected AutoCAD connection profile without calling any CAD tool")
    live.add_argument("--live-documents", action="store_true",
                      help="Check routing between two exact open DWGs without activating their tabs")
    parser.add_argument("--document-paths", nargs=2,
                        help="Two exact full paths of the open DWG copies for --live-documents")
    options = parser.parse_args()
    if options.live_documents and not options.document_paths:
        parser.error("--live-documents requires --document-paths with two full DWG paths")
    try:
        anyio.run(check, options.live_line, options.live_variable,
                  options.inspect_variable, options.list_tools, options.server, options.target, options.dotnet,
                  options.live_documents, options.document_paths)
    except Exception as error:
        print(f"FAIL: {type(error).__name__}: {error}", file=sys.stderr)
        traceback.print_exception(error)
        print("If a mutation timed out, do not rerun automatically; report this output.",
              file=sys.stderr)
        raise SystemExit(1)
