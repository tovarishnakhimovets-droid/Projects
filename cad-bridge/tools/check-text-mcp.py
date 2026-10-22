"""Offline text/style MCP schema check: initialize/tools-list only; no CAD calls."""

import argparse
from collections import Counter
import json
from pathlib import Path

import anyio
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT / "dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll"
STANDARD = "query,modify,drawing,view"
MUTATORS = {"dwg_create_text", "dwg_create_mtext", "dwg_create_entities",
            "dwg_edit_polyline_vertices", "dwg_set_text_style", "dwg_match_properties",
            "dwg_update_texts", "dwg_collapse_and_rewrite", "dwg_create_leader"}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def types(node):
    value = node.get("type", [])
    return {value} if isinstance(value, str) else set(value)


def check_schema(tool, required, fields, arrays=(), readonly=False, idempotent=True):
    schema = tool.inputSchema
    require(types(schema) == {"object"}, f"{tool.name}: expected object input")
    require(set(schema.get("required", [])) == set(required), f"{tool.name}: wrong required fields")
    properties = schema.get("properties", {})
    require(set(properties) == set(fields), f"{tool.name}: wrong input properties")
    for name, expected_type in fields.items():
        require(expected_type in types(properties[name]), f"{tool.name}.{name}: expected {expected_type}")
    for name in arrays:
        items = properties[name].get("items", {})
        require(types(items) == {"string"} and "$ref" not in items,
                f"{tool.name}.{name}: expected direct string items")
    require(tool.annotations is not None, f"{tool.name}: annotations missing")
    require(tool.annotations.readOnlyHint is readonly, f"{tool.name}: wrong readOnlyHint")
    require(tool.annotations.idempotentHint is idempotent, f"{tool.name}: wrong idempotentHint")
    return tool.model_dump(mode="json", by_alias=True, exclude_none=True)


async def listing(options, toolsets, readonly=False):
    args = [str(options.server.resolve()), "--target", str(options.target), "--toolsets", toolsets]
    if readonly:
        args.append("--read-only")
    parameters = StdioServerParameters(command=options.dotnet, args=args, env={
        "BIMWRIGHT_DWG_READ_ONLY": "true" if readonly else "false",
        "BIMWRIGHT_DWG_ENABLE_TOOLBAKER": "false",
        "Logging__LogLevel__Default": "Warning",
    })
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            with anyio.fail_after(15):
                initialized = await session.initialize()
                tools = (await session.list_tools()).tools
    counts = Counter(tool.name for tool in tools)
    require(all(count == 1 for count in counts.values()), f"{toolsets}: duplicate tools: {counts}")
    print(f"PASS: {toolsets}; read_only={readonly}; tools={len(tools)}", flush=True)
    return initialized, tools


async def run(options):
    require(options.server.is_file(), f"Server not found: {options.server}")
    initialized, tools = await listing(options, STANDARD)
    require(len(tools) == 65, f"Standard profile: expected 65 tools, got {len(tools)}")
    by_name = {tool.name: tool for tool in tools}
    require(not ({"dwg_translate_and_rewrite", "dwg_apply_unicode_style"} & by_name.keys()),
            "Removed workflows are still exposed")
    expected = MUTATORS | {"dwg_list_text_styles", "dwg_inspect_block"}
    require(expected <= by_name.keys(), f"Missing tools: {expected - by_name.keys()}")
    schemas = {
        "dwg_list_documents": check_schema(by_name["dwg_list_documents"], (), {}, readonly=True),
        "dwg_use_document": check_schema(by_name["dwg_use_document"], (),
            {"document_id": "string"}, readonly=True),
        "dwg_get_selection": check_schema(by_name["dwg_get_selection"], (),
            {"max_entities": "integer", "include_geometry": "boolean"}, readonly=True),
        "dwg_create_leader": check_schema(by_name["dwg_create_leader"], ("points",),
            {"points": "string", "text": "string", "layer": "string", "color_index": "integer",
             "height": "number", "arrow_size": "number", "text_style": "string"}, idempotent=False),
        "dwg_get_selected_texts": check_schema(by_name["dwg_get_selected_texts"], (),
            {"groupingStrength": "string", "includeEntities": "boolean"}, readonly=True),
        "dwg_update_texts": check_schema(by_name["dwg_update_texts"], ("items",),
            {"items": "string"}),
        "dwg_collapse_and_rewrite": check_schema(by_name["dwg_collapse_and_rewrite"], ("clusters",),
            {"clusters": "string"}, idempotent=False),
        "dwg_inspect_block": check_schema(by_name["dwg_inspect_block"], ("handle",),
            {"handle": "string", "expected_document": "string", "expected_fingerprint": "string",
             "include_definition": "boolean", "max_entities": "integer", "max_metadata_objects": "integer"},
            readonly=True),
        "dwg_list_text_styles": check_schema(by_name["dwg_list_text_styles"], (),
            {"limit": "integer"}, readonly=True),
        "dwg_create_text": check_schema(by_name["dwg_create_text"], ("text", "position"),
            {"text": "string", "position": "string", "height": "number", "rotation": "number",
             "layer": "string", "color_index": "integer", "text_style": "string", "width_factor": "number"},
            idempotent=False),
        "dwg_create_mtext": check_schema(by_name["dwg_create_mtext"], ("text", "location"),
            {"text": "string", "location": "string", "width": "number", "height": "number",
             "rotation": "number", "layer": "string", "color_index": "integer", "text_style": "string"},
            idempotent=False),
        "dwg_create_entities": check_schema(by_name["dwg_create_entities"],
            ("expected_document", "expected_fingerprint", "entities"),
            {"expected_document": "string", "expected_fingerprint": "string", "entities": "array", "dry_run": "boolean"},
            idempotent=False),
        "dwg_edit_polyline_vertices": check_schema(by_name["dwg_edit_polyline_vertices"],
            ("expected_document", "expected_fingerprint", "changes"),
            {"expected_document": "string", "expected_fingerprint": "string", "changes": "array",
             "min_segment_length": "number", "dry_run": "boolean"}, idempotent=False),
        "dwg_set_text_style": check_schema(by_name["dwg_set_text_style"], ("handles", "text_style"),
            {"handles": "array", "text_style": "string"}, arrays=("handles",)),
        "dwg_match_properties": check_schema(by_name["dwg_match_properties"],
            ("source_handle", "target_handles", "properties"),
            {"source_handle": "string", "target_handles": "array", "properties": "array"},
            arrays=("target_handles", "properties")),
    }
    matrix = [{"profile": STANDARD, "read_only": False, "count": len(tools), "tools": sorted(by_name)}]
    _, annotation = await listing(options, STANDARD + ",annotation")
    annotation_names = {tool.name for tool in annotation}
    require(len(annotation) == 66, f"Combined annotation profile: expected 66, got {len(annotation)}")
    require(annotation_names - by_name.keys() == {"dwg_create_table"},
            "Combined annotation profile: unexpected additions")
    require(sum(tool.name == "dwg_create_leader" for tool in annotation) == 1,
            "Combined annotation profile: dwg_create_leader must appear exactly once")
    require(sum(tool.name == "dwg_create_text" for tool in annotation) == 1,
            "Combined annotation profile: dwg_create_text must appear exactly once")
    require(sum(tool.name == "dwg_create_mtext" for tool in annotation) == 1,
            "Combined annotation profile: dwg_create_mtext must appear exactly once")
    matrix.append({"profile": STANDARD + ",annotation", "read_only": False,
                   "count": len(annotation), "tools": sorted(annotation_names)})
    _, readonly = await listing(options, STANDARD, readonly=True)
    readonly_names = {tool.name for tool in readonly}
    require(len(readonly) == 26, f"Read-only profile: expected 26, got {len(readonly)}")
    require("dwg_list_text_styles" in readonly_names, "Read-only profile lost text-style query")
    require(not (MUTATORS & readonly_names), "Read-only profile retained a text/style mutation")
    require(all(tool.annotations is not None and tool.annotations.readOnlyHint is True for tool in readonly),
            "Read-only profile retained a non-read-only tool")
    require(readonly_names == {tool.name for tool in tools if tool.annotations and tool.annotations.readOnlyHint is True},
            "Read-only profile differs from the standard read-only surface")
    matrix.append({"profile": STANDARD, "read_only": True, "count": len(readonly), "tools": sorted(readonly_names)})
    for profile, expected_count in (("query", 19), ("modify", 32), ("drawing", 8),
                                    ("annotation", 4), ("drawing,annotation", 9)):
        _, isolated = await listing(options, profile)
        require(len(isolated) == expected_count, f"{profile}: expected {expected_count}, got {len(isolated)}")
        matrix.append({"profile": profile, "read_only": False, "count": len(isolated),
                       "tools": sorted(tool.name for tool in isolated)})
    result = {"status": "pass", "target": options.target, "server": str(options.server.resolve()),
              "server_info": initialized.serverInfo.model_dump(mode="json", by_alias=True, exclude_none=True),
              "matrix": matrix, "schemas": schemas, "cad_tool_calls": 0}
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS: text/style and block inspection schemas; annotation uniqueness; read-only filtering; no CAD tool was called")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", type=Path, default=SERVER)
    parser.add_argument("--target", type=int, choices=(2026, 2027), default=2026)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path)
    anyio.run(run, parser.parse_args())
