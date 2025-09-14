namespace Dotto.Application.InternalServices.UploadService;

// TODO: this should be moved out into FileUpload.Contracts
public interface IUploadService
{
    public Task<Uri> UploadFile(Stream stream, long fileSize, string? filename, string? contentType, CancellationToken token);
}