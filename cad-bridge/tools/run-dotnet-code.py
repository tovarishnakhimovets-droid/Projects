"""Execute a trusted native C# drawing script through the upstream MCP SDK."""

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


async def run(path, server, target, dotnet):
    if not server.is_file():
        raise FileNotFoundError(server)
    code = path.read_text(encoding="utf-8")
    parameters = StdioServerParameters(
        command=dotnet,
        args=[str(server), "--target", str(target), "--toolsets", "query,code"],
        env={"BIMWRIGHT_DWG_READ_ONLY": "false",
             "BIMWRIGHT_DWG_ENABLE_TOOLBAKER": "false",
             "Logging__LogLevel__Default": "Warning"},
    )
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            with anyio.fail_after(15):
                await session.initialize()
            with anyio.fail_after(50):
                response = await session.call_tool("dwg_send_code", {"code": code})
            text = "\n".join(item.text for item in response.content if item.type == "text")
            if response.isError:
                raise RuntimeError(text)
            payload = json.loads(text)
            if not payload.get("ok"):
                raise RuntimeError(payload.get("error", payload))
            execution = payload["result"]
            if not execution.get("ok"):
                raise RuntimeError(execution.get("error", execution))
            print(json.dumps(execution["result"], ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("script", type=Path)
    parser.add_argument("--server", type=Path, default=SERVER, help="Override the locally built server DLL")
    parser.add_argument("--target", type=int, choices=(2026, 2027), default=2027, help="AutoCAD target year")
    parser.add_argument("--dotnet", default="dotnet", help="dotnet executable (default: PATH)")
    args = parser.parse_args()
    try:
        anyio.run(run, args.script.resolve(), args.server, args.target, args.dotnet)
    except Exception as error:
        traceback.print_exception(error)
        print("Execution may be uncertain after a timeout; do not repeat a drawing mutation.",
              file=sys.stderr)
        raise SystemExit(1)
