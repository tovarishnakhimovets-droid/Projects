using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BatchExecutorTests
    {
        [Fact]
        public void Run_RejectsLispBeforeExecutingAnyBatchItem()
        {
            var executed = 0;
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"run_lisp\",\"params\":{\"code\":\"(princ)\"}}]}"),
                (cmd, parameters) => { executed++; return CommandResult.Success(null); });
            Assert.False(result.Ok);
            Assert.Equal(0, executed);
            Assert.Contains("run_lisp", result.Error);
        }

        [Fact]
        public void Run_RejectsNestedBatch()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"batch_execute\",\"params\":{}}]}"),
                (cmd, parameters) => CommandResult.Success(new { }));

            Assert.False(result.Ok);
            Assert.Contains("nested", result.Error);
        }

        [Fact]
        public void Run_RejectsRunBakedToolInsideBatch()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"run_baked_tool\",\"params\":{}}]}"),
                (cmd, parameters) => CommandResult.Success(new { }));

            Assert.False(result.Ok);
            Assert.Contains("run_baked_tool", result.Error);
        }

        [Fact]
        public void Run_PreflightsForbiddenCommandsBeforeExecutingAnyItem()
        {
            var executed = 0;

            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"run_baked_tool\",\"params\":{}}]}"),
                (cmd, parameters) =>
                {
                    executed++;
                    return CommandResult.Success(new { });
                });

            Assert.False(result.Ok);
            Assert.Equal(0, executed);
            Assert.Contains("run_baked_tool", result.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("{}")]
        [InlineData("{\"dry_run\":true}")]
        [InlineData("{\"dry_run\":false}")]
        [InlineData("{\"dry_run\":\"false\"}")]
        public void Run_RejectsCurrentRootBindBeforeExecutingAnyEarlierItem(string bindParameters)
        {
            var request = new JObject
            {
                ["commands"] = new JArray(
                    new JObject
                    {
                        ["cmd"] = "create_line",
                        ["params"] = new JObject
                        {
                            ["start"] = new JObject { ["x"] = 0, ["y"] = 0 },
                            ["end"] = new JObject { ["x"] = 1, ["y"] = 0 }
                        }
                    },
                    new JObject
                    {
                        ["cmd"] = "bind_current_xref",
                        ["params"] = bindParameters == null ? JValue.CreateNull() : JObject.Parse(bindParameters)
                    })
            };
            var original = request.DeepClone();
            int executed = 0;
            var result = BatchExecutor.Run(request,
                (cmd, parameters) => { executed++; return CommandResult.Success(null); });

            Assert.False(result.Ok);
            Assert.Equal(0, executed);
            Assert.Contains("bind_current_xref", result.Error);
            Assert.Contains("top-level only", result.Error);
            Assert.True(JToken.DeepEquals(original, request));
        }

        [Fact]
        public void Run_DetectsPartialFailureAndKeepsPerItemResults()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"ok\",\"params\":{}},{\"cmd\":\"fail\",\"params\":{}}]}"),
                (cmd, parameters) => cmd == "fail"
                    ? CommandResult.Fail("bad")
                    : CommandResult.Success(new { value = 1 }));

            Assert.True(result.Ok);
            var json = JObject.FromObject(result.Result);
            Assert.True(json.Value<bool>("partial_failure"));
            Assert.False(json.Value<bool>("transactional"));
            Assert.Equal(2, ((JArray)json["results"]).Count);
            Assert.Equal("bad", json["results"][1].Value<string>("error"));
        }
    }
}
