using Dotto.Application.Abstractions.MediaProcessing;
using Dotto.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotto.Application.InternalServices.MediaProcessing;

    public class UrlCorrector(IOptions<UrlCorrectionSettings> config,
        ILogger<UrlCorrector> logger) : IUrlCorrector
    {
        private readonly List<UrlCorrectionRule> _rules = config.Value.Rules;

        public Uri CorrectUrl(Uri url)
        {
            var urlString = url.ToString();

            foreach (var rule in _rules)
            {
                var correctedUrl = rule.CompiledRegexes
                    .Select(regex => regex.Replace(urlString, rule.Replacement))
                    .FirstOrDefault(result => result != urlString); // if the regex didn't match, it'll simply return the input string
                    
                if (correctedUrl == null)
                    continue;
                
                if (Uri.TryCreate(correctedUrl, UriKind.Absolute, out var resultUri))
                    return resultUri;
                
                logger.LogWarning("Corrected source URL {UrlString} to an invalid URL {CorrectedUrl} " +
                                  "(according to rule: {Regex} -> {Replacement}). Ignoring correction.",
                    urlString, correctedUrl, rule.Patterns, rule.Replacement);
            }

            // If no rules matched, return the original URL
            return url;
        }
    }