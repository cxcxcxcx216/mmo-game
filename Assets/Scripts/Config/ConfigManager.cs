using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Minecraft.Config
{
    /// <summary>
    /// 配置表系统入口。统一加载和管理所有静态游戏配置数据。
    /// <para>
    /// 设计要点：
    /// 1. JSON 格式配置数据存放在 Resources/Config/ 目录下，由 config_util 工具从 Excel 生成。
    /// 2. 数据类与 Manager 类由 config_util 工具自动生成，位于 Generated/ 子目录。
    /// 3. 启动时调用 <see cref="LoadAll"/> 一次性加载所有配置表。
    /// 4. 通过 <see cref="GetBlockConfig"/> / <see cref="GetProfessionConfig"/> 等方法访问配置。
    /// 5. 配置数据为只读快照，运行时不可修改。
    /// </para>
    /// </summary>
    public static class ConfigManager
    {
        // ==================== 配置表实例 ====================

        private static BlockConfigManager _blockConfig;
        private static ProfessionConfigManager _professionConfig;
        private static ServerConfigManager _serverConfig;

        /// <summary>方块配置表。</summary>
        public static BlockConfigManager Blocks => _blockConfig;

        /// <summary>职业配置表。</summary>
        public static ProfessionConfigManager Professions => _professionConfig;

        /// <summary>服务器配置表。</summary>
        public static ServerConfigManager Servers => _serverConfig;

        /// <summary>是否已加载。</summary>
        public static bool IsLoaded { get; private set; }

        // ==================== 加载方法 ====================

        /// <summary>
        /// 加载所有配置表。从 Resources/Config/ 目录读取 JSON 数据。
        /// 应在游戏启动时调用一次。
        /// </summary>
        public static void LoadAll()
        {
            _blockConfig = LoadJson<BlockConfigManager>("Config/BlockConfig");
            _professionConfig = LoadJson<ProfessionConfigManager>("Config/ProfessionConfig");
            _serverConfig = LoadJson<ServerConfigManager>("Config/ServerConfig");

            IsLoaded = true;
            Debug.Log($"[ConfigManager] 配置表加载完成: Blocks={Count(_blockConfig)}, " +
                      $"Professions={Count(_professionConfig)}, Servers={Count(_serverConfig)}");
        }

        /// <summary>
        /// 从 Resources 目录加载 JSON 配置表到 Manager 实例。
        /// </summary>
        /// <typeparam name="T">Manager 类型（须有无参构造和 Load(string) 方法）。</typeparam>
        /// <param name="resourcesPath">Resources 相对路径（无扩展名）。</param>
        /// <returns>Manager 实例；加载失败返回空 Manager。</returns>
        private static T LoadJson<T>(string resourcesPath) where T : new()
        {
            var asset = Resources.Load<TextAsset>(resourcesPath);
            if (asset == null)
            {
                Debug.LogError($"[ConfigManager] 配置文件不存在: {resourcesPath}");
                return new T();
            }

            try
            {
                var manager = new T();
                var loadMethod = typeof(T).GetMethod("Load", new[] { typeof(string) });
                if (loadMethod == null)
                {
                    Debug.LogError($"[ConfigManager] {typeof(T).Name} 缺少 Load(string) 方法");
                    return manager;
                }
                // Unity JsonUtility 对字段名大小写敏感，需将 JSON 中小写字段名首字母大写
                string json = PascalCaseJsonKeys(asset.text);
                loadMethod.Invoke(manager, new object[] { json });
                return manager;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigManager] 解析配置文件失败: {resourcesPath}, error={e.Message}");
                return new T();
            }
        }

        /// <summary>统计 Manager 中的数据条数（通过反射调用 GetAll）。</summary>
        private static int Count(object manager)
        {
            if (manager == null) return 0;
            var getAll = manager.GetType().GetMethod("GetAll");
            if (getAll == null) return 0;
            var result = getAll.Invoke(manager, null) as System.Collections.IEnumerable;
            if (result == null) return 0;
            int count = 0;
            foreach (var item in result) count++;
            return count;
        }

        /// <summary>
        /// 将 JSON 中的字段名首字母大写（PascalCase），以匹配 Unity JsonUtility 的大小写敏感字段。
        /// 例如 {"id": 1} → {"Id": 1}。仅处理 key，不影响字符串值。
        /// </summary>
        private static string PascalCaseJsonKeys(string json)
        {
            return Regex.Replace(json, @"""([a-z])((?:\w*)?)""\s*:",
                m => "\"" + char.ToUpper(m.Groups[1].Value[0]) + m.Groups[2].Value + "\":");
        }

        // ==================== 便捷访问 ====================

        /// <summary>获取方块配置。</summary>
        public static BlockConfig GetBlockConfig(int blockId)
        {
            return _blockConfig?.GetById(blockId);
        }

        /// <summary>获取职业配置。</summary>
        public static ProfessionConfig GetProfessionConfig(int professionId)
        {
            return _professionConfig?.GetById(professionId);
        }

        /// <summary>获取默认服务器配置（recommended=true 的第一条，否则第一条）。</summary>
        public static ServerConfig GetDefaultServer()
        {
            if (_serverConfig == null) return null;
            foreach (var server in _serverConfig.GetAll())
            {
                if (server.Recommended)
                    return server;
            }
            foreach (var server in _serverConfig.GetAll())
                return server;
            return null;
        }
    }
}
