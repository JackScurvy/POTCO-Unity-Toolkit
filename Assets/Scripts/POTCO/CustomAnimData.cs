namespace POTCO
{
    /// <summary>
    /// Data structure for a single AnimSet definition from CustomAnims.py
    /// </summary>
    [System.Serializable]
    public class CustomAnimData
    {
        public string animSetName;

        // Animation lists (can have multiple options)
        public System.Collections.Generic.List<string> idles = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> interactInto = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> interact = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> interactOutof = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> noticeIdle = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> notice1 = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> notice2 = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> greeting = new System.Collections.Generic.List<string>();

        // Prop attachment
        public System.Collections.Generic.List<PropData> props = new System.Collections.Generic.List<PropData>();
    }

    /// <summary>
    /// Data structure for prop attachment information
    /// </summary>
    [System.Serializable]
    public class PropData
    {
        public string modelPath;
        public int propType; // 0 = DYNAMIC, 1 = PERSIST

        public PropData(string path, int type = 0)
        {
            modelPath = path;
            propType = type;
        }
    }
}
