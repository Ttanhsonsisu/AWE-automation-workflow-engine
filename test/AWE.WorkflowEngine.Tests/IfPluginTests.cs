using AWE.Sdk.v2;
using AWE.WorkflowEngine.BuiltInPlugins;

namespace AWE.WorkflowEngine.Tests;

public class IfPluginTests
{
    private readonly IfPlugin _sut = new();

    [Theory]
    [InlineData("{\"Value1\":true,\"Operator\":\"==\",\"Value2\":\"true\"}", true)]
    [InlineData("{\"Value1\":false,\"Operator\":\"==\",\"Value2\":\"true\"}", false)]
    [InlineData("{\"Value1\":10,\"Operator\":\">\",\"Value2\":\"5\"}", true)]
    [InlineData("{\"Value1\":\"AWE Engine\",\"Operator\":\"contains\",\"Value2\":\"engine\"}", true)]
    [InlineData("{\"Value1\":\"READY\",\"Operator\":\"==\",\"Value2\":\"ready\"}", true)]
    public async Task ExecuteAsync_HandlesResolvedScalarTypes(string payload, bool expectedIsMatch)
    {
        var result = await _sut.ExecuteAsync(new PluginContext(payload, CancellationToken.None));

        Assert.True(result.IsSuccess);
        var outputs = Assert.IsType<Dictionary<string, object>>(result.Outputs);
        Assert.Equal(expectedIsMatch, Assert.IsType<bool>(outputs["IsMatch"]));
    }
}
