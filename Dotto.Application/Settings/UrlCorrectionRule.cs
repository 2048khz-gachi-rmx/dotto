using System.Text.RegularExpressions;
using Dotto.Common;

namespace Dotto.Application.Settings;

public class UrlCorrectionRule
{
    public List<string> Patterns { get; set; } = [];
    public string Replacement { get; set; } = string.Empty;
    
    private List<Regex>? _compiledRegexes;
    
    /// <remarks>
    /// The Regex object is compiled and cached on first access.
    /// </remarks>
    
    // p.s. don't try to stick this into a constructor, or options binding will break with validations (options first call ctor, then bind the values)
    public List<Regex> CompiledRegexes => LazyInitializer.EnsureInitialized(ref _compiledRegexes, () =>
        {
            if (Patterns.IsNullOrEmpty())
                throw new InvalidOperationException("Correction rule patterns cannot be null or empty.");

            return Patterns
                .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                .ToList();
        });
}