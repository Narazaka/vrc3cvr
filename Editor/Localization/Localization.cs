using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace PeanutTools_VRC3CVR.Localization
{
    static class Localization
    {
        const string LocaleKey = "PeanutTools_VRC3CVR_Locale";
        public struct Locale
        {
            public string Name;
            public string DisplayName;
            public Locale(string name, string displayName)
            {
                Name = name;
                DisplayName = displayName;
            }
        }
        public static readonly Locale[] Locales = new Locale[]
        {
            new Locale("en-US", "English"),
            new Locale("ja-JP", "日本語"),
        };
        public static string[] LocaleNames => System.Array.ConvertAll(Locales, locale => locale.Name);
        public static string[] LocaleDisplayNames => System.Array.ConvertAll(Locales, locale => locale.DisplayName);
        public static string CurrentLocale = EditorPrefs.GetString(LocaleKey, CultureInfo.CurrentCulture.Name);

        public static void DrawLocaleSelector()
        {
            EditorGUI.BeginChangeCheck();
            var prevLocaleIndex = System.Array.IndexOf(LocaleNames, CurrentLocale);
            EditorGUIUtility.labelWidth = 80;
            var localeIndex = EditorGUILayout.Popup("Language", prevLocaleIndex < 0 ? 0 : prevLocaleIndex, LocaleDisplayNames, GUILayout.Width(180));
            EditorGUIUtility.labelWidth = 0;
            if (EditorGUI.EndChangeCheck())
            {
                if (localeIndex < 0 || localeIndex >= Locales.Length)
                {
                    localeIndex = 0;
                }
                CurrentLocale = Locales[localeIndex].Name;
                EditorPrefs.SetString(LocaleKey, CurrentLocale);
            }
        }
    }
}
