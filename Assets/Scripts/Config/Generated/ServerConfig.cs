// =====================================================================
// 此文件由 config_util 工具自动生成，请勿手动修改。
// 生成时间：2026-07-11
// 源文件：config_util/excel/MMOConfig.xlsx → Sheet: ServerConfig
// =====================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.Config
{
    [Serializable]
    public class ServerConfig
    {
        public int Id; // int
        public string Name; // string
        public string Host; // string
        public int Port; // int
        public bool Recommended; // bool
    }

    [Serializable]
    public class ServerConfigContainer
    {
        public List<ServerConfig> rows;
    }

    public class ServerConfigManager
    {
        private Dictionary<int, ServerConfig> _data = new Dictionary<int, ServerConfig>();

        public void Load(string json)
        {
            var container = JsonUtility.FromJson<ServerConfigContainer>("{\"rows\":" + json + "}");
            _data.Clear();
            if (container != null && container.rows != null)
            {
                foreach (var row in container.rows)
                {
                    _data[row.Id] = row;
                }
            }
        }

        public ServerConfig GetById(int id)
        {
            _data.TryGetValue(id, out var val);
            return val;
        }

        public Dictionary<int, ServerConfig>.ValueCollection GetAll()
        {
            return _data.Values;
        }

        public void Reload(string json)
        {
            _data.Clear();
            Load(json);
        }
    }
}
