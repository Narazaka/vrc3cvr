using UnityEngine;

namespace PeanutTools_VRC3CVR.Localization
{
#pragma warning disable IDE1006
    class istring
#pragma warning restore IDE1006
    {
        public string en;
        public string ja;
        public istring(string en, string ja)
        {
            this.en = en;
            this.ja = string.Join("\u200B", ja.Split());
        }
        public GUIContent GUIContent => new GUIContent(this);

        public static implicit operator string(istring data) => IsJa ? data.ja : data.en;

        static bool IsJa =>
#if UNITY_EDITOR
            Localization.CurrentLocale == "ja-JP";
#else
            false;
#endif
    }
}
