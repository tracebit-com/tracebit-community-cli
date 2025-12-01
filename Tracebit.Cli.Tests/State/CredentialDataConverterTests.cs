using System.Buffers;
using System.Text;
using System.Text.Json;

using Tracebit.Cli.State;

namespace Tracebit.Cli.Tests.State;

public class CredentialDataConverterTests
{
    [Fact]
    public void Read_RetainsExtraFields()
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("""{"type": "invalid", "futureField": "something", "name": "future credential", "createdAt": "2025-12-25T03:00:00Z", "expiresAt": null, "path": null}"""));
        var result = new CredentialDataConverter().Read(ref reader, typeof(CredentialData), StateJsonSerializerContext.Default.Options);
        Assert.NotNull(result);

        Assert.Contains("futureField", result.ExtraFields);
        Assert.Equal("something", result.ExtraFields["futureField"].GetString());
    }

    [Fact]
    public void Read_RetainsUnknownType()
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("""{"type": "invalid", "name": "future credential", "createdAt": "2025-12-25T03:00:00Z", "expiresAt": null, "path": null}"""));
        var result = new CredentialDataConverter().Read(ref reader, typeof(CredentialData), StateJsonSerializerContext.Default.Options);
        Assert.NotNull(result);

        Assert.Equal("invalid", result.TypeName);
    }

    [Fact]
    public void Write_RetainsExtraFields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer);
        new CredentialDataConverter().Write(writer, new UnknownCredentialData
        {
            Name = "test",
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now,
            Path = "",
            ExtraFields = new Dictionary<string, JsonElement> { { "test", JsonSerializer.SerializeToElement("someExtraValue") } }
        }, StateJsonSerializerContext.Default.Options);

        Assert.Contains("someExtraValue", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    [Fact]
    public void Write_RetainsUnkownType()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer);
        new CredentialDataConverter().Write(writer, new UnknownCredentialData
        {
            TypeName = "someUnknownType",
            Name = "test",
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now,
            Path = "",
        }, StateJsonSerializerContext.Default.Options);

        Assert.Contains("someUnknownType", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}
