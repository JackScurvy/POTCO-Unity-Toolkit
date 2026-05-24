using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PandaEggTag
{
    public string key;
    public string value;
}

[DisallowMultipleComponent]
public class PandaEggNodeMetadata : MonoBehaviour
{
    public string dcsType = "";
    public bool isModelNode;
    public bool isSwitch;
    public float switchFps;
    public string billboardType = "";

    public string collisionName = "";
    public string collisionType = "";
    public string[] collisionFlags = Array.Empty<string>();
    public bool hasCollideMask;
    public int collideMask;
    public string collideMaskRaw = "";

    public bool hasLodDistance;
    public float lodNearDistance;
    public float lodFarDistance;

    public List<PandaEggTag> tags = new List<PandaEggTag>();

    public bool HasAnyData
    {
        get
        {
            return !string.IsNullOrEmpty(dcsType) ||
                   isModelNode ||
                   isSwitch ||
                   !string.IsNullOrEmpty(billboardType) ||
                   !string.IsNullOrEmpty(collisionType) ||
                   hasCollideMask ||
                   hasLodDistance ||
                   tags.Count > 0;
        }
    }

    public string GetTagValue(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i].key, key, StringComparison.Ordinal))
            {
                return tags[i].value;
            }
        }

        return null;
    }

    public void AddOrReplaceTag(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i].key, key, StringComparison.Ordinal))
            {
                tags[i] = new PandaEggTag { key = key, value = value };
                return;
            }
        }

        tags.Add(new PandaEggTag { key = key, value = value });
    }
}
