"""Manual upstream MCP check; live modes explicitly enable scratch-drawing edits."""

import argparse
import json
from pathlib import Path
import sys
import traceback

import anyio
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / "upstream/dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll"


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


async def check(live_line, live_variable, inspect_variable, list_tools, server, target, dotnet):
    if not server.is_file():
        raise FileNotFoundError(server)
    # Listing the write tool's schema requires registering it, but invokes no CAD handler.
    writable = live_line or live_variable or inspect_variable or list_tools
    toolsets = ("query,modify,drawing,view" if list_tools else
                "query,drawing" if live_variable or inspect_variable else
                "query,modify" if live_line else "query")
    parameters = StdioServerParameters(
        command=dotnet,
        args=[str(server), "--target", str(target), "--toolsets", toolsets],
        env={"BIMWRIGHT_DWG_READ_ONLY": "false" if writable else "true"},
    )
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            with anyio.fail_after(15):
                initialized = await session.initialize()
            print(f"MCP: {initialized.serverInfo.name} {initialized.serverInfo.version}")
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
    options = parser.parse_args()
    try:
        anyio.run(check, options.live_line, options.live_variable,
                  options.inspect_variable, options.list_tools, options.server, options.target, options.dotnet)
    except Exception as error:
        print(f"FAIL: {type(error).__name__}: {error}", file=sys.stderr)
        traceback.print_exception(error)
        print("If a mutation timed out, do not rerun automatically; report this output.",
              file=sys.stderr)
        raise SystemExit(1)
