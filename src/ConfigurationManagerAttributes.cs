using System;
using BepInEx.Configuration;

namespace SVSPregnancy
{
    // Token: 0x0200000B RID: 11
    internal sealed class ConfigurationManagerAttributes
    {
        // Token: 0x04000058 RID: 88
        public bool? ShowRangeAsPercent;

        // Token: 0x04000059 RID: 89
        public Action<ConfigEntryBase> CustomDrawer;

        // Token: 0x0400005A RID: 90
        public bool? Browsable;

        // Token: 0x0400005B RID: 91
        public string Category;

        // Token: 0x0400005C RID: 92
        public object DefaultValue;

        // Token: 0x0400005D RID: 93
        public bool? HideDefaultButton;

        // Token: 0x0400005E RID: 94
        public string Description;

        // Token: 0x0400005F RID: 95
        public string DispName;

        // Token: 0x04000060 RID: 96
        public int? Order;

        // Token: 0x04000061 RID: 97
        public bool? ReadOnly;

        // Token: 0x04000062 RID: 98
        public bool? IsAdvanced;

        // Token: 0x04000063 RID: 99
        public Func<object, string> ObjToStr;

        // Token: 0x04000064 RID: 100
        public Func<string, object> StrToObj;
    }
}
