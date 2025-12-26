namespace Dotto.Application.Abstractions.MediaProcessing;

public interface IUrlCorrector
{
    Uri CorrectUrl(Uri url);
}