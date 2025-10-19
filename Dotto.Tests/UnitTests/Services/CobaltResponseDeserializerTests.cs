using System.Text.Json;
using Dotto.Infrastructure.Downloader.CobaltDownloader;
using Dotto.Infrastructure.Downloader.CobaltDownloader.Response;
using Shouldly;

namespace Dotto.Tests.UnitTests.Services;

public class CobaltResponseDeserializerTests
{
    private readonly CobaltResponseDeserializer _deserializer = new();

    [Test]
    public void Deserialize_WithErrorStatus_ReturnsErrorEnum()
    {
        // Arrange
        var json = @"{""status"": ""error""}";

        // Act
        var result = CobaltResponseDeserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltErrorResponse>();
    }

    [Test]
    public void Deserialize_WithTunnelStatus_ReturnsTunnelEnum()
    {
        // Arrange
        var json = @"{""status"": ""classic""}";

        // Act
        var result = CobaltResponseDeserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltTunnelResponse>();
    }

    [Test]
    public void Deserialize_WithLocalProcessingStatus_ReturnsLocalProcessingEnum()
    {
        // Arrange
        var json = @"{""status"": ""local-processing""}";

        // Act
        var result = CobaltResponseDeserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltLocalProcessingResponse>();
    }

    [Test]
    public void Deserialize_WithPickerStatus_ReturnsPickerEnum()
    {
        // Arrange
        var json = @"{""status"": ""picker""}";

        // Act
        var result = CobaltResponseDeserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltPickerResponse>();
    }

    [Test]
    public void Deserialize_WithInvalidStatus_ThrowsException()
    {
        // Arrange
        var json = @"{""status"": ""invalid-status""}";

        // Act & Assert
        var exception = Should.Throw<JsonException>(() => CobaltResponseDeserializer.DeserializeToCobaltResponse(json));
        exception.Message.ShouldContain("The JSON value could not be converted to"); // TODO: how do i make this a nicer message?
    }
}