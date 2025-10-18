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
        string json = @"{""status"": ""error""}";

        // Act
        var result = _deserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltErrorResponse>();
    }

    [Test]
    public void Deserialize__WithTunnelStatus_ReturnsTunnelEnum()
    {
        // Arrange
        string json = @"{""status"": ""classic""}";

        // Act
        var result = _deserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltTunnelResponse>();
    }

    [Test]
    public void Deserialize_WithLocalProcessingStatus_ReturnsLocalProcessingEnum()
    {
        // Arrange
        string json = @"{""status"": ""local-processing""}";

        // Act
        var result = _deserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltLocalProcessingResponse>();
    }

    [Test]
    public void Deserialize_WithPickerStatus_ReturnsPickerEnum()
    {
        // Arrange
        string json = @"{""status"": ""picker""}";

        // Act
        var result = _deserializer.DeserializeToCobaltResponse(json);

        // Assert
        result.ShouldBeOfType<CobaltPickerResponse>();
    }

    [Test]
    public void Deserialize_WithInvalidStatus_ThrowsException()
    {
        // Arrange
        string json = @"{""status"": ""invalid-status""}";

        // Act & Assert
        var exception = Should.Throw<JsonException>(() => _deserializer.DeserializeToCobaltResponse(json));
        exception.Message.ShouldContain("The JSON value could not be converted to"); // TODO: how do i make this a nicer message?
    }
}