using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ManyFace.CatCafe
{
    /// <summary>WebGL 与桌面版共用的 Supabase 排行榜 REST 客户端；连接参数全部来自 Settings 表。</summary>
    internal static class CatCafeLeaderboard
    {
        [Serializable]
        internal sealed class ScoreRow
        {
            public string name;
            public int score;
            public int days;
            public bool endless;
            public string created_at;
        }

        internal static bool Enabled { get { return CatCafeConfigDatabase.GetRequiredBool("leaderboard_enabled"); } }

        internal static IEnumerator Submit(string playerName, int score, int days, bool endless,
            Action<bool, string> completed)
        {
            if (!Enabled) { completed(false, "disabled"); yield break; }
            ScoreRow row = new ScoreRow
            {
                name = string.IsNullOrWhiteSpace(playerName)
                    ? CatCafeConfigDatabase.GetRequiredString("ui_leaderboard_default_name")
                    : playerName.Trim(),
                score = score,
                days = days,
                endless = endless,
                created_at = DateTime.UtcNow.ToString("o")
            };
            using (UnityWebRequest request = CreateRequest("POST", string.Empty))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(row)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Prefer", "return=minimal");
                yield return request.SendWebRequest();
                completed(request.result == UnityWebRequest.Result.Success,
                    request.result == UnityWebRequest.Result.Success ? string.Empty : request.error);
            }
        }

        internal static IEnumerator Fetch(Action<ScoreRow[], string> completed)
        {
            yield return Fetch(0, completed);
        }

        /// <summary>按页读取成绩，offset 由排行榜界面维护，避免一次下载全部记录。</summary>
        internal static IEnumerator Fetch(int offset, Action<ScoreRow[], string> completed)
        {
            if (!Enabled) { completed(new ScoreRow[0], "disabled"); yield break; }
            string query = "?select=name,score,days,endless,created_at&order=score.desc&limit=" +
                CatCafeConfigDatabase.GetRequiredInt("leaderboard_limit") + "&offset=" + Mathf.Max(0, offset);
            using (UnityWebRequest request = CreateRequest("GET", query))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed(new ScoreRow[0], request.error);
                    yield break;
                }
                try { completed(JsonHelper.FromJson<ScoreRow>(request.downloadHandler.text), string.Empty); }
                catch (Exception exception) { completed(new ScoreRow[0], exception.Message); }
            }
        }

        /// <summary>同分并列：排名为严格高于本局分数的记录数加一。</summary>
        internal static IEnumerator FetchRank(int score, Action<int, string> completed)
        {
            if (!Enabled) { completed(0, "disabled"); yield break; }
            string query = "?select=score&score=gt." + score;
            using (UnityWebRequest request = CreateRequest("GET", query))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Prefer", "count=exact");
                request.SetRequestHeader("Range", "0-0");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed(0, request.error);
                    yield break;
                }

                string contentRange = request.GetResponseHeader("Content-Range");
                int slash = string.IsNullOrEmpty(contentRange) ? -1 : contentRange.LastIndexOf('/');
                int higherScores;
                if (slash < 0 || !int.TryParse(contentRange.Substring(slash + 1), out higherScores))
                {
                    completed(0, "missing count");
                    yield break;
                }
                completed(higherScores + 1, string.Empty);
            }
        }

        private static UnityWebRequest CreateRequest(string method, string suffix)
        {
            string url = CatCafeConfigDatabase.GetRequiredString("leaderboard_url").TrimEnd('/') +
                "/rest/v1/" + CatCafeConfigDatabase.GetRequiredString("leaderboard_table") + suffix;
            UnityWebRequest request = new UnityWebRequest(url, method);
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(CatCafeConfigDatabase.GetRequiredFloat("leaderboard_timeout_seconds")));
            request.SetRequestHeader("apikey", CatCafeConfigDatabase.GetRequiredString("leaderboard_publishable_key"));
            return request;
        }

        [Serializable] private sealed class ArrayWrapper<T> { public T[] rows; }
        private static class JsonHelper
        {
            internal static T[] FromJson<T>(string json)
            {
                ArrayWrapper<T> result = JsonUtility.FromJson<ArrayWrapper<T>>("{\"rows\":" + json + "}");
                return result != null && result.rows != null ? result.rows : new T[0];
            }
        }
    }
}
