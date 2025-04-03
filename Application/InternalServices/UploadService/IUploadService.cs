namespace Dotto.Application.InternalServices.UploadService;

public interface IUploadService
{
    public Task<Uri> UploadFile(Stream stream, string? filename, string? contentType);
}