using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Globalization;
using System.Text.Json;

namespace PoproshaykaBot.Core.Chat.Display;

public sealed class ChatDisplayStore(ILogger<ChatDisplayStore> logger)
{
    public const double DefaultZoom = 0.75;
    public const double MinZoom = 0.25;
    public const double MaxZoom = 5.0;

    private const string HideClutterScriptTemplate = """
                                                     (function () {
                                                         const selectors = __SELECTORS__;
                                                         const wrapperClasses = ['tw-transition', 'tw-callout', 'consent-banner'];
                                                         const findWrapper = (el) => {
                                                             let node = el;
                                                             for (let i = 0; i < 15 && node; i++) {
                                                                 if (node.classList && wrapperClasses.some((c) => node.classList.contains(c))) {
                                                                     return node;
                                                                 }
                                                                 node = node.parentElement;
                                                             }
                                                             return el;
                                                         };
                                                         const hideAll = () => {
                                                             const consentAccept = document.querySelector('[data-a-target="consent-banner-accept"]');
                                                             if (consentAccept) {
                                                                 consentAccept.click();
                                                             }
                                                             for (const selector of selectors) {
                                                                 document.querySelectorAll(selector).forEach((el) => {
                                                                     const wrapper = findWrapper(el);
                                                                     if (wrapper.dataset.poproshaykaHidden !== '1') {
                                                                         wrapper.dataset.poproshaykaHidden = '1';
                                                                         wrapper.style.setProperty('display', 'none', 'important');
                                                                     }
                                                                 });
                                                             }
                                                         };
                                                         hideAll();
                                                         if (document.readyState === 'loading') {
                                                             document.addEventListener('DOMContentLoaded', hideAll, { once: true });
                                                         }
                                                         const startObserver = () => {
                                                             if (document.documentElement) {
                                                                 new MutationObserver(hideAll).observe(document.documentElement, { childList: true, subtree: true });
                                                             } else {
                                                                 setTimeout(startObserver, 0);
                                                             }
                                                         };
                                                         startObserver();
                                                     })();
                                                     """;

    private static readonly string[] BuiltInClutterSelectors =
    [
        "[data-a-target=\"consent-banner\"]",
        ".consent-banner",
        ".tw-callout-message",
        "[class*=\"channelLeaderboardHeader\"]",
        "[class*=\"channelLeaderboardBottomIconContainer\"]",
        "[class*=\"community-highlight\"]",
    ];

    private static readonly string ZoomFilePath = AppPaths.Combine("chat-zoom.txt");

    private static readonly string BlockersFilePath = AppPaths.Combine("chat-blockers.txt");

    public double LoadZoom()
    {
        try
        {
            if (!File.Exists(ZoomFilePath))
            {
                return DefaultZoom;
            }

            var text = File.ReadAllText(ZoomFilePath).Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)
                && zoom >= MinZoom && zoom <= MaxZoom)
            {
                return zoom;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось прочитать сохранённый масштаб чата");
        }

        return DefaultZoom;
    }

    public void SaveZoom(double zoom)
    {
        try
        {
            AtomicFile.Save(ZoomFilePath, zoom.ToString("F3", CultureInfo.InvariantCulture), logger);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось сохранить масштаб чата");
        }
    }

    public string LoadBlockersText()
    {
        try
        {
            return File.Exists(BlockersFilePath) ? File.ReadAllText(BlockersFilePath) : string.Empty;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Не удалось прочитать список блокираторов баннеров чата");
            return string.Empty;
        }
    }

    public bool TrySaveBlockersText(string text)
    {
        try
        {
            AtomicFile.Save(BlockersFilePath, text, logger);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось сохранить список блокираторов баннеров чата");
            return false;
        }
    }

    public string BuildHideClutterScript()
    {
        var selectors = BuiltInClutterSelectors.Concat(LoadUserSelectors()).ToArray();
        return HideClutterScriptTemplate.Replace("__SELECTORS__", JsonSerializer.Serialize(selectors), StringComparison.Ordinal);
    }

    private IEnumerable<string> LoadUserSelectors()
    {
        return LoadBlockersText()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
    }
}
