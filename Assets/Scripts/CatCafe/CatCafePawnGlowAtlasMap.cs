using System;
using System.Collections.Generic;
using UnityEngine;

namespace ManyFace.CatCafe
{
    [CreateAssetMenu(
        menuName = "Cat Cafe/Pawn Glow Atlas Map",
        fileName = "CatCafePawnGlowAtlasMap")]
    public sealed class CatCafePawnGlowAtlasMap : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string spriteName;
            public int atlasIndex;
            public Rect uvRect;
        }

        [SerializeField] private int contentSize = 128;
        [SerializeField] private List<Texture2D> atlases = new List<Texture2D>();
        [SerializeField] private List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<string, Entry> lookup;

        public int ContentSize => Mathf.Max(1, contentSize);
        public int AtlasCount => atlases == null ? 0 : atlases.Count;

        public bool TryGetRegion(
            string spriteName,
            out Texture2D atlas,
            out Rect uvRect)
        {
            atlas = null;
            uvRect = default;

            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            EnsureLookup();
            if (!lookup.TryGetValue(spriteName, out Entry entry) ||
                atlases == null ||
                entry.atlasIndex < 0 ||
                entry.atlasIndex >= atlases.Count)
            {
                return false;
            }

            atlas = atlases[entry.atlasIndex];
            uvRect = entry.uvRect;
            return atlas != null;
        }

        public void SetGeneratedData(
            int generatedContentSize,
            List<Texture2D> generatedAtlases,
            List<Entry> generatedEntries)
        {
            contentSize = Mathf.Max(1, generatedContentSize);
            atlases = generatedAtlases ?? new List<Texture2D>();
            entries = generatedEntries ?? new List<Entry>();
            lookup = null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.spriteName))
                {
                    continue;
                }

                lookup[entry.spriteName] = entry;
            }
        }
    }
}
