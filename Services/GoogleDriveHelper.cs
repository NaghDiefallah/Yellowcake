using System;
using System.Text.RegularExpressions;

namespace Yellowcake.Services;

public static class GoogleDriveHelper
{
    private static readonly Regex FileIdRegex = new(@"\/d\/([a-zA-Z0-9_-]+)", RegexOptions.Compiled);
    private static readonly Regex ConfirmRegex = new(@"confirm=([a-zA-Z0-9_-]+)", RegexOptions.Compiled);

    public static bool IsGoogleDriveUrl(string url)
    {
        return !string.IsNullOrEmpty(url) && 
               (url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase));
    }

    public static string ConvertToDirectDownloadUrl(string url)
    {
        if (!IsGoogleDriveUrl(url))
            return url;

        var match = FileIdRegex.Match(url);
        if (match.Success)
        {
            var fileId = match.Groups[1].Value;
            return $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
        }

        return url;
    }

    public static string? ExtractFileId(string url)
    {
        var match = FileIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ExtractConfirmToken(string html)
    {
        var match = ConfirmRegex.Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsVirusScanWarning(string html)
    {
        return html.Contains("Google Drive - Virus scan warning", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("can't be scanned with Google's virus scanner", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("download anyway", StringComparison.OrdinalIgnoreCase);
    }
}