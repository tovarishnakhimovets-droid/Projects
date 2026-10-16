"""Manual style-tool check. --live edits a scratch drawing; never switches tabs or saves."""
import argparse
import json
from pathlib import Path
import sys
import traceback
import uuid

import anyio
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / "upstream/dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll"

def require(condition, detail):
    if not condition:
        raise RuntimeError(detail)

def parameters(server, toolsets, target, dotnet):
    return StdioServerParameters(command=dotnet,
        args=[str(server), "--target", str(target), "--toolsets", toolsets],
        env={"BIMWRIGHT_DWG_READ_ONLY": "false", "BIMWRIGHT_DWG_ENABLE_TOOLBAKER": "false",
             "Logging__LogLevel__Default": "Warning"})

class Check:
    def __init__(self, session, server, document, target, dotnet):
        self.session, self.server, self.document = session, server, document
        self.target, self.dotnet = target, dotnet
        self.layer = "MCP_STYLE_" + uuid.uuid4().hex
        self.handles, self.layer_handle = [], None
        self.layer_created, self.uncertain = False, False

    async def guard(self):
        info = await self.call("dwg_get_drawing_info")
        require(info["document_name"] == self.document,
                f"Drawing changed: expected {self.document!r}, received {info['document_name']!r}")
        require(info["current_space"] == "model", "Use model space and keep it active during this check")
        if self.layer_created:
            require(any(row["name"] == self.layer for row in (await self.call("dwg_list_layers"))["layers"]), "Scratch layer absent; possible drawing switch")

    async def call(self, name, args=None, *, mutate=False, refusal=False):
        if mutate:
            require(not self.uncertain, "Execution unknown; no further mutations are allowed")
            await self.guard()
        try:
            with anyio.fail_after(40):
                response = await self.session.call_tool(name, args or {})
            text = "\n".join(block.text for block in response.content if block.type == "text")
            require(not response.isError, f"{name}: MCP error: {text}")
            payload = json.loads(text)
            require(isinstance(payload, dict) and (payload.get("ok") is not True or isinstance(payload.get("result"), dict)), f"{name}: malformed response: {payload}")
        except BaseException:
            if mutate:
                self.uncertain = True
            raise
        if mutate and any(word in json.dumps(payload).lower() for word in
                          ("execution_unknown", "timed out", "timeout", "connection lost", "disconnected", "plugin closed connection", "plugin communication error")):
            self.uncertain = True
            raise RuntimeError(f"{name}: execution unknown: {payload}")
        if payload.get("ok") is True and name in (
                "dwg_create_line", "dwg_create_circle", "dwg_create_rectangle", "dwg_create_hatch"):
            handle = payload.get("result", {}).get("handle")
            if not handle:
                self.uncertain = True
                raise RuntimeError(f"Created entity has no returned handle: {payload}")
            self.handles.append(handle)
            print("CREATED:", name, handle, flush=True)
        if refusal:
            require(payload.get("ok") is False, f"{name}: expected refusal: {payload}")
            require(bool(payload.get("error")), f"{name}: refusal lacks error: {payload}")
            return payload["error"]
        require(payload.get("ok") is True, f"{name}: {payload}")
        result = payload["result"]
        for key in ("results", "entities"):
            if key in result:
                require(all(item.get("ok", True) is True for item in result[key]), f"{name}: {result}")
        return result

    async def entity(self, handle):
        result = await self.call("dwg_get_entity_properties",
            {"handles": json.dumps([handle]), "includeGeometry": True})
        require(len(result["entities"]) == 1, f"Unexpected entity records: {result}")
        entity = result["entities"][0]["entity"]
        require(entity["handle"] == handle and entity["layer"] == self.layer, f"Wrong entity: {entity}")
        return entity
    async def own_handles(self):
        result = await self.call("dwg_query_entities", {"layer": self.layer, "limit": 10})
        return sorted(entity["handle"] for entity in result["entities"])

    async def cleanup(self):
        if self.uncertain:
            print("CLEANUP LIMITED: execution unknown; no mutation is repeated.", file=sys.stderr)
            return
        if self.handles:
            require(set(self.handles) <= set(await self.own_handles()), "Scratch-layer ownership mismatch; erase refused")
            result = await self.call("dwg_erase_entities",
                {"handles": json.dumps(list(reversed(self.handles)))}, mutate=True)
            require(len(result["results"]) == len(self.handles), f"Incomplete erase response: {result}")
            print("ERASED:", self.handles, flush=True)
        if not self.layer_created:
            return
        # Only the uniquely created layer is eligible; scan every block for remaining entities.
        code = f'''if (System.IO.Path.GetFileName(doc.Name) != {json.dumps(self.document)})
    throw new System.InvalidOperationException("Drawing changed; cleanup refused");
using (var tx = db.TransactionManager.StartTransaction()) {{
    var lt = (Autodesk.AutoCAD.DatabaseServices.LayerTable)tx.GetObject(db.LayerTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
    if (!lt.Has({json.dumps(self.layer)})) return new {{ removed = true, already_absent = true }};
    var lid = lt[{json.dumps(self.layer)}];
    var layer = (Autodesk.AutoCAD.DatabaseServices.LayerTableRecord)tx.GetObject(lid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
    if (layer.Handle.ToString() != {json.dumps(self.layer_handle)} || db.Clayer == lid)
        throw new System.InvalidOperationException("Layer identity/current layer mismatch; cleanup refused");
    var bt = (Autodesk.AutoCAD.DatabaseServices.BlockTable)tx.GetObject(db.BlockTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
    foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId bid in bt) {{
        var block = (Autodesk.AutoCAD.DatabaseServices.BlockTableRecord)tx.GetObject(bid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
        foreach (Autodesk.AutoCAD.DatabaseServices.ObjectId eid in block) {{
            if (eid.IsErased) continue;
            var entity = tx.GetObject(eid, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
            if (entity != null && entity.LayerId == lid) throw new System.InvalidOperationException("Test layer is not empty; cleanup refused");
        }}
    }}
    layer.UpgradeOpen(); layer.Erase(); tx.Commit(); return new {{ removed = true }};
}}'''
        async with stdio_client(parameters(self.server, "query,modify,code", self.target, self.dotnet)) as (read, write):
            async with ClientSession(read, write) as session:
                with anyio.fail_after(15):
                    await session.initialize()
                self.session = session
                result = await self.call("dwg_send_code", {"code": code}, mutate=True)
                require(result.get("ok") is True and result["result"].get("removed") is True,
                        f"Layer cleanup failed: {result}")
        print("REMOVED LAYER:", self.layer, flush=True)

    async def live(self):
        print("SCRATCH:", self.document, self.layer, flush=True)
        try:
            await self.guard()
            existing = await self.call("dwg_list_layers")
            require(all(layer["name"] != self.layer for layer in existing["layers"]), "GUID layer already exists")
            layer = await self.call("dwg_create_layer", {"name": self.layer}, mutate=True)
            require(layer.get("created") is True, f"Layer was not newly created: {layer}")
            self.layer_created, self.layer_handle = True, layer["handle"]
            point = lambda x, y: json.dumps({"x": x, "y": y, "z": 0})
            line = (await self.call("dwg_create_line", {"start": point(0, 0), "end": point(10, 0), "layer": self.layer}, mutate=True))["handle"]
            circle = (await self.call("dwg_create_circle", {"center": point(20, 0), "radius": 5, "layer": self.layer}, mutate=True))["handle"]
            rectangle = (await self.call("dwg_create_rectangle", {"corner1": point(30, 0), "corner2": point(40, 10), "layer": self.layer}, mutate=True))["handle"]
            result = await self.call("dwg_set_layer_lineweight", {"layers": [self.layer], "lineweight_mm": 0.30}, mutate=True)
            require(result["count"] == len(result["layers"]) and set(result["layers"]) <= {self.layer} and
                    result["mode"] == "explicit" and result["lineweight_mm"] == 0.30, f"Layer setter: {result}")
            layers = (await self.call("dwg_list_layers"))["layers"]
            actual = next(layer for layer in layers if layer["name"] == self.layer)
            require(actual["lineweight_mm"] == 0.30 and actual["lineweight_mode"] == "explicit", f"Layer readback: {actual}")
            for mode, mm in (("explicit", 0.30), ("by_layer", None), ("by_block", None)):
                args = {"handles": [line], "mode": mode}
                if mm is not None:
                    args["lineweight_mm"] = mm
                result = await self.call("dwg_set_lineweight", args, mutate=True)
                require(result["count"] == len(result["handles"]) and set(result["handles"]) <= {line} and
                        result["mode"] == mode and result["lineweight_mm"] == mm, f"Entity setter: {result}")
                actual = await self.entity(line)
                require(actual["lineweight_mode"] == mode and actual["lineweight_mm"] == mm, f"Line readback: {actual}")
            before, original = await self.own_handles(), await self.entity(line)
            require(before == sorted(self.handles), f"Unexpected scratch-layer entities: {before}")
            error = await self.call("dwg_set_lineweight", {"handles": [line], "lineweight_mm": 0.31}, mutate=True, refusal=True)
            require("lineweight" in error, f"Unexpected refusal: {error}")
            error = await self.call("dwg_create_hatch", {"boundary_handle": line}, mutate=True, refusal=True)
            require("boundary" in error.lower(), f"Unexpected boundary refusal: {error}")
            require(await self.own_handles() == before and await self.entity(line) == original, "Refusal changed scratch objects")
            for boundary, color, order in ((circle, {"color_rgb": [255, 255, 255]}, "above_entities"),
                                            (rectangle, {"color_index": 30}, "below_entities")):
                result = await self.call("dwg_create_hatch", {"boundary_handle": boundary, "layer": self.layer,
                    "associative": True, "draw_order": order, **color}, mutate=True)
                require(result["draw_order"] == order, f"Wrong draw order: {result}")
                actual = await self.entity(result["handle"])
                require(actual["type"] == "Hatch" and actual["pattern"] == "SOLID" and
                        actual["associative"] is True and actual["solid_fill"] is True, f"Hatch readback: {actual}")
                require(actual["color_rgb"] == [255, 255, 255] if "color_rgb" in color else
                        actual["color_index"] == 30 and actual["color_rgb"] is None, f"Hatch color: {actual}")
            print("PASS: layer/entity lineweights, SOLID circle/rectangle hatches, unchanged refusals", flush=True)
        finally:
            failed = sys.exc_info()[0] is not None
            try:
                await self.cleanup()
            except BaseException as error:
                print("CLEANUP LIMITED: inspect the reported scratch objects manually.", file=sys.stderr)
                traceback.print_exception(error)
                if not failed:
                    raise
            finally:
                print("SCRATCH IDENTIFIERS:", json.dumps({"document": self.document, "layer": self.layer,
                    "layer_handle": self.layer_handle, "handles": self.handles}, ensure_ascii=False), flush=True)

async def run(options):
    require(options.server.is_file(), f"Server not found: {options.server}")
    async with stdio_client(parameters(options.server, "query,modify,drawing,view", options.target, options.dotnet)) as (read, write):
        async with ClientSession(read, write) as session:
            with anyio.fail_after(15):
                initialized = await session.initialize()
                listing = await session.list_tools()
            print(f"MCP: {initialized.serverInfo.name} {initialized.serverInfo.version}; TOOLS: {len(listing.tools)}")
            for name, mandatory, field, item_type in (
                    ("dwg_set_lineweight", {"handles"}, "handles", "string"),
                    ("dwg_set_layer_lineweight", {"layers", "lineweight_mm"}, "layers", "string"),
                    ("dwg_create_hatch", {"boundary_handle"}, "color_rgb", "integer")):
                tool = next((tool for tool in listing.tools if tool.name == name), None)
                require(tool is not None, f"Missing tool: {name}")
                schema = tool.inputSchema
                array = schema["properties"][field]
                require(set(schema["required"]) == mandatory and "array" in array["type"] and
                        item_type in array["items"]["type"] and "$ref" not in array["items"], f"Bad schema: {name}: {schema}")
                print("SCHEMA:", name, json.dumps(schema, ensure_ascii=False))
            if options.live:
                await Check(session, options.server, options.document, options.target, options.dotnet).live()
            else:
                print("PASS: 3 tool schemas; no CAD tool was called")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", type=Path, default=SERVER, help="Override the locally built server DLL")
    parser.add_argument("--target", type=int, choices=(2026, 2027), default=2027, help="AutoCAD target year")
    parser.add_argument("--dotnet", default="dotnet", help="dotnet executable (default: PATH)")
    parser.add_argument("--live", action="store_true", help="Opt in to scratch edits and cleanup; keep this drawing active")
    parser.add_argument("--document", help="Exact active document_name (including .dwg); required with --live")
    options = parser.parse_args()
    if options.live and not options.document:
        parser.error("--live requires --document; use a free session and do not switch drawing tabs")
    try:
        anyio.run(run, options)
    except BaseException as error:
        traceback.print_exception(error)
        print("FAIL: do not repeat uncertain mutations; inspect SCRATCH IDENTIFIERS and cleanup limitations.", file=sys.stderr)
        raise SystemExit(1)
