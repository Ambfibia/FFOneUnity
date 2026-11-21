using System;
using System.Linq;
using FFOne.Content;

namespace FFOne.UI
{
    public sealed partial class GameplayUiCatalog
    {
        internal (int first, int last)[] GuideTopics, GuidePages;
        internal (int type, int size, int text)[] GuideContent;
        (string name, string comment)[] guideTitles, guideStrings;
        (int topic, int page)[] firstUsePages;
        (int text, int duration)[] firstUseData;
        (string message, string cue)[] firstUseStrings;

        internal (string message, string cue, int duration) FirstUseMessage(int condition, string language)
        {
            if ((uint)condition >= firstUseData.Length) return default;
            var entry = firstUseData[condition];
            if (entry.text <= 0 || entry.text >= firstUseStrings.Length) return default;
            var line = firstUseStrings[entry.text];
            return (Localize("content.tabledata.first_use.first_use_string." + entry.text + ".str_comment", line.message, language),
                line.cue, entry.duration);
        }

        (int level, int greeting)[] mentorMessages;
        string[] mentorStrings;
        internal string MentorMessage(int guide, bool levelUp, string language)
        {
            if (guide <= 0 || guide >= mentorMessages.Length) return "";
            int line = levelUp ? mentorMessages[guide].level : mentorMessages[guide].greeting;
            if (line <= 0 || line >= mentorStrings.Length) return "";
            return Localize("content.tabledata.guide.guide_string." + line + ".sz_string", mentorStrings[line], language);
        }

        void ReadGuide(NativeDataDocument document)
        {
            int help = Child(document, 0, "m_pHelpTable");
            int[] Table(string key) => Rows(document, Child(document, help, key));
            int N(int row, string field) => Number(document, row, field);
            (string, string) Strings(int row) => (Text(document, row, "m_strName"), Text(document, row, "m_strComment"));

            GuideTopics = Table("m_pHelpData").Select(r => (N(r, "m_iTitleStartString"), N(r, "m_iSubEndString"))).ToArray();
            GuidePages = Table("m_pHelpPageData").Select(r => (N(r, "m_iStartContent"), N(r, "m_iEndContent"))).ToArray();
            GuideContent = Table("m_pHelpPageDescData").Select(r => (N(r, "m_iType"), N(r, "m_iSize"), N(r, "m_iString"))).ToArray();
            guideTitles = Table("m_pHelpString").Select(Strings).ToArray();
            guideStrings = Table("m_pHelpPageString").Select(Strings).ToArray();
            int guide = Child(document, 0, "m_pGuideTable");
            mentorMessages = Rows(document, Child(document, guide, "m_pGuideData"))
                .Select(row => (N(row, "m_iLevelUp"), N(row, "m_iLoginNomail"))).ToArray();
            mentorStrings = Rows(document, Child(document, guide, "m_pGuideStringData"))
                .Select(row => Text(document, row, "m_pszString")).ToArray();
            int firstUse = Child(document, 0, "m_pFirstUseTable");
            var conditions = Rows(document, Child(document, firstUse, "m_pFirstUseData"));
            firstUseData = conditions.Select(r => (N(r, "m_iString"), N(r, "m_iCompDuration"))).ToArray();
            firstUseStrings = Rows(document, Child(document, firstUse, "m_pFirstUseString"))
                .Select(r => (Text(document, r, "m_strComment"), Text(document, r, "m_strComment1"))).ToArray();
            firstUsePages = conditions
                .Select(r => (N(r, "m_iHelpMain"), N(r, "m_iHelpSub"))).ToArray();
        }

        internal bool GuideStart(int? firstUse, out int topic, out int page)
        {
            topic = 1;
            page = GuideTopics.Length > 1 ? GuideTopics[1].first : 0;
            if (firstUse.HasValue)
            {
                if ((uint)firstUse.Value >= firstUsePages.Length) return false;
                (topic, page) = firstUsePages[firstUse.Value];
            }
            return topic > 0 && topic < GuideTopics.Length && page >= GuideTopics[topic].first &&
                page <= GuideTopics[topic].last && page < GuidePages.Length;
        }

        internal string GuideTitle(int id, bool section, string language) =>
            Localize("content.tabledata.help.help_string." + id + (section ? ".str_comment" : ".str_name"),
                section ? guideTitles[id].comment : guideTitles[id].name, language);

        internal string GuideText(int id, string language) => Localize(
            "content.tabledata.help.help_page_string." + id + ".str_name", guideStrings[id].name, language);

        internal string GuideScreenshot(int id)
        {
            string file = guideStrings[id].comment.Trim();
            // The unresolved primary "lteminfo" route (lowercase L) has no published texture.
            if (!file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || file.Equals("lteminfo.jpg", StringComparison.OrdinalIgnoreCase)) return null;
            return "ui/en/gameplay/game-guide/screenshots/" + file.Substring(0, file.Length - 4).Replace(' ', '-').ToLowerInvariant();
        }
    }
}
