using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "WorldGen/Prefab Registry")]
public class PrefabRegistry : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string key;  
        public GameObject prefab;  
    }

    public List<Entry> entries = new();

    private Dictionary<string, ushort> _keyToId;
    private Dictionary<ushort, GameObject> _idToPrefab;

    public void BuildCaches()
    {
        _keyToId = new Dictionary<string, ushort>(StringComparer.Ordinal);
        _idToPrefab = new Dictionary<ushort, GameObject>();
        for (ushort i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (string.IsNullOrEmpty(e.key) || e.prefab == null) continue;
            _keyToId[e.key] = i;  
            _idToPrefab[i] = e.prefab;
        }
    }

    public ushort KeyToId(string key) => _keyToId[key];
    public bool TryKeyToId(string key, out ushort id) => _keyToId.TryGetValue(key, out id);
    public GameObject GetPrefab(ushort id) => _idToPrefab[id];
}
