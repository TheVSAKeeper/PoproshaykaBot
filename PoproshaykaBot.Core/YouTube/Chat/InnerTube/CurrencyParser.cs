// Copyright (c) 2025 Agash Thamo. Licensed under the MIT License. See THIRD-PARTY-NOTICES.txt.

using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal static partial class CurrencyParser
{
    private static readonly Dictionary<string, string> XmlMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = "USD",
        ["A$"] = "AUD",
        ["CA$"] = "CAD",
        ["CFPF"] = "XPF",
        ["CN¥"] = "CNY",
        ["EC$"] = "XCD",
        ["F CFA"] = "XOF",
        ["F\u202FCFA"] = "XOF",
        ["FCFA"] = "XAF",
        ["HK$"] = "HKD",
        ["MX$"] = "MXN",
        ["NT$"] = "TWD",
        ["NZ$"] = "NZD",
        ["R$"] = "BRL",
        ["£"] = "GBP",
        ["¥"] = "JPY",
        ["₩"] = "KRW",
        ["₪"] = "ILS",
        ["₫"] = "VND",
        ["€"] = "EUR",
        ["₱"] = "PHP",
        ["₹"] = "INR",
    };

    private static readonly Dictionary<string, string> ClosureFallbackMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AU$"] = "AUD",
        ["Tk"] = "BDT",
        ["lev"] = "BGN",
        ["FrCD"] = "CDF",
        ["CL$"] = "CLP",
        ["RMB¥"] = "CNY",
        ["COL$"] = "COP",
        ["Kč"] = "CZK",
        ["kr."] = "DKK",
        ["RD$"] = "DOP",
        ["LE"] = "EGP",
        ["Birr"] = "ETB",
        ["GB£"] = "GBP",
        ["kn"] = "HRK",
        ["Ft"] = "HUF",
        ["Rp"] = "IDR",
        ["IL₪"] = "ILS",
        ["Rs"] = "INR",
        ["JP¥"] = "JPY",
        ["KR₩"] = "KRW",
        ["SLRs"] = "LKR",
        ["Lt"] = "LTL",
        ["MN₮"] = "MNT",
        ["Rf"] = "MVR",
        ["Mex$"] = "MXN",
        ["RM"] = "MYR",
        ["NOkr"] = "NOK",
        ["kr"] = "SEK",
        ["B/."] = "PAB",
        ["S/."] = "PEN",
        ["PKRs."] = "PKR",
        ["zł"] = "PLN",
        ["din"] = "RSD",
        ["SAR"] = "SAR",
        ["S$"] = "SGD",
        ["TSh"] = "TZS",
        ["грн."] = "UAH",
        ["US$"] = "USD",
        ["$U"] = "UYU",
        ["VN₫"] = "VND",
        ["Af."] = "AFN",
        ["dram"] = "AMD",
        ["NAf."] = "ANG",
        ["Kz"] = "AOA",
        ["AR$"] = "ARS",
        ["Afl."] = "AWG",
        ["KM"] = "BAM",
        ["Bds$"] = "BBD",
        ["FBu"] = "BIF",
        ["BD$"] = "BMD",
        ["B$"] = "BND",
        ["Bs"] = "BOB",
        ["BS$"] = "BSD",
        ["Nu."] = "BTN",
        ["pula"] = "BWP",
        ["BZ$"] = "BZD",
        ["UF"] = "CLF",
        ["CUC$"] = "CUC",
        ["CU$"] = "CUP",
        ["Esc"] = "CVE",
        ["Fdj"] = "DJF",
        ["Nfk"] = "ERN",
        ["FJ$"] = "FJD",
        ["FK£"] = "FKP",
        ["GHS"] = "GHS",
        ["GI£"] = "GIP",
        ["FG"] = "GNF",
        ["Q"] = "GTQ",
        ["GY$"] = "GYD",
        ["L"] = "HNL",
        ["Ksh"] = "KES",
        ["KGS"] = "KGS",
        ["Riel"] = "KHR",
        ["CF"] = "KMF",
        ["KY$"] = "KYD",
        ["L$"] = "LRD",
        ["LSL"] = "LSL",
        ["LD"] = "LYD",
        ["dh"] = "MAD",
        ["MDL"] = "MDL",
        ["Ar"] = "MGA",
        ["K"] = "MMK",
        ["MOP$"] = "MOP",
        ["MURs"] = "MUR",
        ["MWK"] = "MWK",
        ["MTn"] = "MZN",
        ["N$"] = "NAD",
        ["NG₦"] = "NGN",
        ["C$"] = "CAD",
        ["NPRs"] = "NPR",
        ["QR"] = "QAR",
        ["RF"] = "RWF",
        ["SI$"] = "SBD",
        ["SH£"] = "SHP",
        ["SR$"] = "SRD",
        ["SY£"] = "SYP",
        ["T$"] = "TOP",
        ["TT$"] = "TTD",
        ["soʻm"] = "UZS",
        ["VT"] = "VUV",
        ["WS$"] = "WST",
        ["Z$"] = "ZWD",
    };

    private static readonly FrozenDictionary<string, string> SymbolToCode = BuildMap()
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static (decimal AmountValue, string? Currency) Parse(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return (0m, null);
        }

        var normalizedInput = NormalizeSpaces(rawInput);
        var numberMatch = NumberExtractor().Match(normalizedInput);

        if (!numberMatch.Success)
        {
            return (0m, ResolveCurrencyCode(normalizedInput, normalizedInput));
        }

        var amountValue = ParseDecimal(numberMatch.Value);
        var before = normalizedInput[..numberMatch.Index];
        var after = normalizedInput[(numberMatch.Index + numberMatch.Length)..];
        var symbolPart = string.Concat(before, after).Trim();
        var currencyCode = ResolveCurrencyCode(symbolPart, normalizedInput);
        return (amountValue, currencyCode);
    }

    private static Dictionary<string, string> BuildMap()
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in XmlMap)
        {
            merged[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in ClosureFallbackMap)
        {
            merged.TryAdd(kvp.Key, kvp.Value);
        }

        merged.TryAdd("kr", "SEK");
        return merged;
    }

    private static decimal ParseDecimal(string input)
    {
        var normalized = NormalizeAmountToken(input);
        return decimal.TryParse(
            normalized,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var amount)
            ? amount
            : 0m;
    }

    private static string? ResolveCurrencyCode(string symbolPart, string rawInput)
    {
        var normalizedSymbol = NormalizeSpaces(symbolPart).Replace(" ", string.Empty, StringComparison.Ordinal);

        if (SymbolToCode.TryGetValue(normalizedSymbol, out var directCode))
        {
            return directCode;
        }

        if (IsIsoCode(normalizedSymbol))
        {
            return normalizedSymbol.ToUpperInvariant();
        }

        var embeddedSymbolCode = IsoCodeRegex().Match(normalizedSymbol.ToUpperInvariant());

        if (embeddedSymbolCode.Success)
        {
            return embeddedSymbolCode.Value;
        }

        var embeddedRawCode = IsoCodeRegex().Match(rawInput.ToUpperInvariant());

        if (embeddedRawCode.Success)
        {
            return embeddedRawCode.Value;
        }

        return string.Equals(normalizedSymbol, "$", StringComparison.Ordinal)
            ? "USD"
            : string.Equals(normalizedSymbol, "¥", StringComparison.Ordinal)
                ? "JPY"
                : null;
    }

    private static string NormalizeSpaces(string value) =>
        value.Replace('\u00A0', ' ').Replace('\u202F', ' ').Trim();

    private static bool IsIsoCode(string value) =>
        value.Length == 3
        && char.IsAsciiLetterUpper(value[0])
        && char.IsAsciiLetterUpper(value[1])
        && char.IsAsciiLetterUpper(value[2]);

    private static string NormalizeAmountToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        var normalized = NormalizeSpaces(token).Replace(" ", string.Empty, StringComparison.Ordinal);
        var hasComma = normalized.Contains(',', StringComparison.Ordinal);
        var hasDot = normalized.Contains('.', StringComparison.Ordinal);

        if (hasComma && hasDot)
        {
            return normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal)
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        if (hasComma)
        {
            var commaCount = normalized.Count(static c => c == ',');

            if (commaCount > 1)
            {
                return normalized.Replace(",", string.Empty, StringComparison.Ordinal);
            }

            var commaIndex = normalized.LastIndexOf(',');
            var digitsAfter = normalized.Length - commaIndex - 1;
            return digitsAfter == 3
                ? normalized.Replace(",", string.Empty, StringComparison.Ordinal)
                : normalized.Replace(",", ".", StringComparison.Ordinal);
        }

        if (hasDot)
        {
            var dotCount = normalized.Count(static c => c == '.');

            if (dotCount > 1)
            {
                var lastDotIndex = normalized.LastIndexOf('.');
                var integerPart = normalized[..lastDotIndex].Replace(".", string.Empty, StringComparison.Ordinal);
                var decimalPart = normalized[(lastDotIndex + 1)..];
                return decimalPart.Length == 3
                    ? integerPart + decimalPart
                    : integerPart + "." + decimalPart;
            }
        }

        return normalized;
    }

    [GeneratedRegex(@"[\d\.,\s\u00A0\u202F]+", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1_000)]
    private static partial Regex NumberExtractor();

    [GeneratedRegex(@"\b[A-Z]{3}\b", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1_000)]
    private static partial Regex IsoCodeRegex();
}
