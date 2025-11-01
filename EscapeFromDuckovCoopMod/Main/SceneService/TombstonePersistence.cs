// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team
//
// This program is not a free software.
// It's distributed under a license based on AGPL-3.0,
// with strict additional restrictions:
//  YOU MUST NOT use this software for commercial purposes.
//  YOU MUST NOT use this software to run a headless game server.
//  YOU MUST include a conspicuous notice of attribution to
//  Mr-sans-and-InitLoader-s-team/Escape-From-Duckov-Coop-Mod-Preview as the original author.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod
{
    /// <summary>
    /// 序列化安全的Vector3结构
    /// </summary>
    [Serializable]
    public struct SerializableVector3
    {
        public float x, y, z;
        
        public SerializableVector3(Vector3 vector)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
        }
        
        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
        
        public static implicit operator SerializableVector3(Vector3 vector)
        {
            return new SerializableVector3(vector);
        }
        
        public static implicit operator Vector3(SerializableVector3 serializable)
        {
            return serializable.ToVector3();
        }
    }

    /// <summary>
    /// 序列化安全的Quaternion结构
    /// </summary>
    [Serializable]
    public struct SerializableQuaternion
    {
        public float x, y, z, w;
        
        public SerializableQuaternion(Quaternion quaternion)
        {
            x = quaternion.x;
            y = quaternion.y;
            z = quaternion.z;
            w = quaternion.w;
        }
        
        public Quaternion ToQuaternion()
        {
            return new Quaternion(x, y, z, w);
        }
        
        public static implicit operator SerializableQuaternion(Quaternion quaternion)
        {
            return new SerializableQuaternion(quaternion);
        }
        
        public static implicit operator Quaternion(SerializableQuaternion serializable)
        {
            return serializable.ToQuaternion();
        }
    }

    /// <summary>
    /// 墓碑数据结构
    /// </summary>
    [Serializable]
    public class TombstoneData
    {
        public int lootUid;
        public string sceneId;
        public SerializableVector3 position;
        public SerializableQuaternion rotation;
        public int aiId;
        public long createTime;
        public List<TombstoneItem> items = new List<TombstoneItem>();
    }

    /// <summary>
    /// 墓碑物品数据
    /// </summary>
    [Serializable]
    public class TombstoneItem
    {
        public int position;
        public LootNet.ItemSnapshot snapshot;
    }

    /// <summary>
    /// 用户墓碑数据集合
    /// </summary>
    [Serializable]
    public class UserTombstoneData
    {
        public string userId;
        public List<TombstoneData> tombstones = new List<TombstoneData>();
    }

    /// <summary>
    /// 墓碑数据持久化管理器
    /// </summary>
    public class TombstonePersistence : MonoBehaviour
    {
        public static TombstonePersistence Instance;
        
        private string _streamingAssetsPath;
        private Dictionary<string, UserTombstoneData> _userDataCache = new Dictionary<string, UserTombstoneData>();
        
        private NetService Service => NetService.Instance;
        private bool IsServer => Service != null && Service.IsServer;
        private bool networkStarted => Service != null && Service.networkStarted;

        public void Init()
        {
            Instance = this;
            Debug.Log("[TOMBSTONE] TombstonePersistence Init() called");
            
            // 使用 EnsureDirectoryExists 来初始化和创建目录
            EnsureDirectoryExists();
            
            Debug.Log($"[TOMBSTONE] TombstonePersistence initialized with path: {_streamingAssetsPath}");
        }

        /// <summary>
        /// 获取用户数据文件路径
        /// </summary>
        private string GetUserDataPath(string userId)
        {
            // 使用Base64编码确保文件名安全且可逆
            var safeUserId = EncodeUserIdForFileName(userId);
            return Path.Combine(_streamingAssetsPath, $"{safeUserId}_tombstones.json");
        }

        /// <summary>
        /// 将用户ID编码为安全的文件名
        /// </summary>
        private string EncodeUserIdForFileName(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return "unknown";
            }

            try
            {
                // 使用Base64编码，确保文件名安全且可逆
                var bytes = System.Text.Encoding.UTF8.GetBytes(userId);
                var base64 = Convert.ToBase64String(bytes);
                
                // Base64可能包含 '/' 和 '+' 字符，替换为文件名安全的字符
                // 同时移除填充字符 '='
                var safeBase64 = base64.Replace('/', '_')
                                       .Replace('+', '-')
                                       .TrimEnd('=');
                
                // 添加前缀以便识别这是编码后的用户ID
                return $"user_{safeBase64}";
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to encode userId '{userId}': {e}");
                // 如果编码失败，使用哈希值作为备用方案
                return $"hash_{userId.GetHashCode():X8}";
            }
        }

        /// <summary>
        /// 从文件名解码用户ID
        /// </summary>
        public string DecodeUserIdFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            try
            {
                // 移除文件扩展名和后缀
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                if (nameWithoutExt.EndsWith("_tombstones"))
                {
                    nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - "_tombstones".Length);
                }

                // 检查是否是编码后的用户ID
                if (nameWithoutExt.StartsWith("user_"))
                {
                    var safeBase64 = nameWithoutExt.Substring("user_".Length);
                    
                    // 恢复Base64字符
                    var base64 = safeBase64.Replace('_', '/')
                                          .Replace('-', '+');
                    
                    // 添加必要的填充字符
                    var padding = (4 - (base64.Length % 4)) % 4;
                    base64 += new string('=', padding);
                    
                    // 解码
                    var bytes = Convert.FromBase64String(base64);
                    return System.Text.Encoding.UTF8.GetString(bytes);
                }
                else if (nameWithoutExt.StartsWith("hash_"))
                {
                    // 哈希值无法反向解码，返回原始文件名作为标识
                    return nameWithoutExt;
                }
                else
                {
                    // 可能是旧格式的文件名，直接返回
                    return nameWithoutExt;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to decode fileName '{fileName}': {e}");
                return fileName;
            }
        }

        /// <summary>
        /// 确保墓碑数据目录存在
        /// </summary>
        private void EnsureDirectoryExists()
        {
            try
            {
                if (string.IsNullOrEmpty(_streamingAssetsPath))
                {
                    _streamingAssetsPath = Path.Combine(Application.streamingAssetsPath, "TombstoneData");
                    Debug.Log($"[TOMBSTONE] Initialized _streamingAssetsPath: {_streamingAssetsPath}");
                }
                
                if (!Directory.Exists(_streamingAssetsPath))
                {
                    Debug.Log($"[TOMBSTONE] Directory does not exist, creating: {_streamingAssetsPath}");
                    Directory.CreateDirectory(_streamingAssetsPath);
                    Debug.Log($"[TOMBSTONE] Successfully created directory: {_streamingAssetsPath}");
                }
                else
                {
                    Debug.Log($"[TOMBSTONE] Directory already exists: {_streamingAssetsPath}");
                }
                
                // 验证目录确实存在
                if (!Directory.Exists(_streamingAssetsPath))
                {
                    Debug.LogError($"[TOMBSTONE] Directory creation failed, directory still does not exist: {_streamingAssetsPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to ensure directory exists {_streamingAssetsPath}: {e}");
                
                // 尝试使用备用路径
                try
                {
                    var fallbackPath = Path.Combine(Application.persistentDataPath, "TombstoneData");
                    Debug.LogWarning($"[TOMBSTONE] Trying fallback path: {fallbackPath}");
                    
                    if (!Directory.Exists(fallbackPath))
                    {
                        Directory.CreateDirectory(fallbackPath);
                        Debug.Log($"[TOMBSTONE] Created fallback directory: {fallbackPath}");
                    }
                    
                    _streamingAssetsPath = fallbackPath;
                    Debug.Log($"[TOMBSTONE] Using fallback path: {_streamingAssetsPath}");
                }
                catch (Exception fallbackEx)
                {
                    Debug.LogError($"[TOMBSTONE] Fallback directory creation also failed: {fallbackEx}");
                }
            }
        }

        /// <summary>
        /// 加载用户墓碑数据
        /// </summary>
        public UserTombstoneData LoadUserData(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                Debug.LogWarning("[TOMBSTONE] LoadUserData: userId is null or empty");
                return new UserTombstoneData { userId = userId };
            }

            // 先检查缓存
            if (_userDataCache.TryGetValue(userId, out var cachedData))
            {
                Debug.Log($"[TOMBSTONE] Loaded user data from cache: userId={userId}, tombstones={cachedData.tombstones.Count}");
                
                // 输出缓存中每个墓碑的详细信息
                for (int i = 0; i < cachedData.tombstones.Count; i++)
                {
                    var tombstone = cachedData.tombstones[i];
                    Debug.Log($"[TOMBSTONE] Cache tombstone {i}: lootUid={tombstone.lootUid}, sceneId={tombstone.sceneId}, items={tombstone.items.Count}");
                }
                
                return cachedData;
            }

            var filePath = GetUserDataPath(userId);
            
            try
            {
                // 确保目录存在
                EnsureDirectoryExists();
                
                if (File.Exists(filePath))
                {
                    Debug.Log($"[TOMBSTONE] Reading tombstone file: {filePath}");
                    var json = File.ReadAllText(filePath);
                    var userData = JsonConvert.DeserializeObject<UserTombstoneData>(json);
                    
                    if (userData == null)
                    {
                        userData = new UserTombstoneData { userId = userId };
                        Debug.LogWarning($"[TOMBSTONE] Failed to deserialize user data, created empty: {userId}");
                    }
                    else
                    {
                        userData.userId = userId; // 确保userId正确
                        Debug.Log($"[TOMBSTONE] Successfully loaded user data from file: userId={userId}, tombstones={userData.tombstones.Count}");
                        
                        // 输出每个墓碑的详细信息
                        for (int i = 0; i < userData.tombstones.Count; i++)
                        {
                            var tombstone = userData.tombstones[i];
                            Debug.Log($"[TOMBSTONE] File tombstone {i}: lootUid={tombstone.lootUid}, sceneId={tombstone.sceneId}, items={tombstone.items.Count}, position={tombstone.position}");
                            
                            // 输出每个物品的详细信息
                            for (int j = 0; j < tombstone.items.Count; j++)
                            {
                                var item = tombstone.items[j];
                                Debug.Log($"[TOMBSTONE]   Item {j}: position={item.position}, typeId={item.snapshot.typeId}, stack={item.snapshot.stack}");
                            }
                        }
                    }
                    
                    _userDataCache[userId] = userData;
                    return userData;
                }
                else
                {
                    Debug.Log($"[TOMBSTONE] Tombstone file does not exist: {filePath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to load user data for {userId}: {e}");
            }

            // 创建新的用户数据
            var newUserData = new UserTombstoneData { userId = userId };
            _userDataCache[userId] = newUserData;
            Debug.Log($"[TOMBSTONE] Created new user data: userId={userId}");
            return newUserData;
        }

        /// <summary>
        /// 保存用户墓碑数据
        /// </summary>
        public void SaveUserData(string userId, UserTombstoneData userData)
        {
            if (string.IsNullOrEmpty(userId) || userData == null)
            {
                Debug.LogWarning("[TOMBSTONE] SaveUserData: invalid parameters");
                return;
            }

            var filePath = GetUserDataPath(userId);
            
            try
            {
                // 确保目录存在
                EnsureDirectoryExists();
                
                userData.userId = userId; // 确保userId正确
                
                // 配置JSON序列化设置，避免循环引用
                var settings = new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                };
                
                var json = JsonConvert.SerializeObject(userData, settings);
                File.WriteAllText(filePath, json);
                
                // 更新缓存
                _userDataCache[userId] = userData;
                
                Debug.Log($"[TOMBSTONE] Saved user data: {userId}, tombstones: {userData.tombstones.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to save user data for {userId}: {e}");
            }
        }   
     /// <summary>
        /// 添加墓碑数据
        /// </summary>
        public void AddTombstone(string userId, int lootUid, string sceneId, Vector3 position, Quaternion rotation, int aiId, Inventory inventory)
        {
            if (!IsServer || string.IsNullOrEmpty(userId))
            {
                Debug.LogWarning("[TOMBSTONE] AddTombstone: not server or invalid userId");
                return;
            }

            try
            {
                var userData = LoadUserData(userId);
                
                // 检查是否已存在相同的墓碑
                var existingTombstone = userData.tombstones.Find(t => t.lootUid == lootUid);
                if (existingTombstone != null)
                {
                    Debug.LogWarning($"[TOMBSTONE] Tombstone already exists: userId={userId}, lootUid={lootUid}");
                    return;
                }

                var tombstone = new TombstoneData
                {
                    lootUid = lootUid,
                    sceneId = sceneId,
                    position = position,
                    rotation = rotation,
                    aiId = aiId,
                    createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                // 序列化物品数据
                if (inventory != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        var item = inventory.GetItemAt(i);
                        if (item != null)
                        {
                            try
                            {
                                var snapshot = ItemTool.MakeSnapshot(item);
                                tombstone.items.Add(new TombstoneItem
                                {
                                    position = i,
                                    snapshot = snapshot
                                });
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[TOMBSTONE] Failed to serialize item at position {i}: {e}");
                            }
                        }
                    }
                }

                userData.tombstones.Add(tombstone);
                SaveUserData(userId, userData);
                
                Debug.Log($"[TOMBSTONE] Added tombstone: userId={userId}, lootUid={lootUid}, sceneId={sceneId}, items={tombstone.items.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to add tombstone: {e}");
            }
        }

        /// <summary>
        /// 从墓碑中移除指定的剩余物品（客户端死亡时上报的所有剩余物品）
        /// 这是减法操作：墓碑物品 - 客户端剩余物品 = 真正应该掉落的物品
        /// </summary>
        public void RemoveEquipmentFromTombstone(string userId, int lootUid, List<int> remainingItemTypeIds)
        {
            if (!IsServer || string.IsNullOrEmpty(userId) || remainingItemTypeIds == null || remainingItemTypeIds.Count == 0)
            {
                Debug.LogWarning($"[TOMBSTONE] RemoveEquipmentFromTombstone early return - IsServer={IsServer}, userId={userId}, remainingItems={remainingItemTypeIds?.Count ?? 0}");
                return;
            }

            try
            {
                Debug.Log($"[TOMBSTONE] Starting subtraction operation for tombstone: userId={userId}, lootUid={lootUid}");
                
                var userData = LoadUserData(userId);
                var tombstone = userData.tombstones.Find(t => t.lootUid == lootUid);
                
                if (tombstone == null)
                {
                    Debug.LogWarning($"[TOMBSTONE] Tombstone not found for subtraction: userId={userId}, lootUid={lootUid}");
                    return;
                }

                var originalCount = tombstone.items.Count;
                var removedCount = 0;
                
                Debug.Log($"[TOMBSTONE] Tombstone before subtraction: {originalCount} items");

                // 为每个剩余物品TypeID移除一个对应的物品（减法操作）
                foreach (var remainingTypeId in remainingItemTypeIds)
                {
                    var itemToRemove = tombstone.items.Find(item => item.snapshot.typeId == remainingTypeId);
                    if (itemToRemove != null)
                    {
                        tombstone.items.Remove(itemToRemove);
                        removedCount++;
                        Debug.Log($"[TOMBSTONE] Subtracted remaining item from tombstone: TypeID={remainingTypeId}, lootUid={lootUid}");
                    }
                    else
                    {
                        Debug.LogWarning($"[TOMBSTONE] Remaining item not found in tombstone (may be pet inventory item that shouldn't be subtracted): TypeID={remainingTypeId}, lootUid={lootUid}");
                    }
                }

                if (removedCount > 0)
                {
                    SaveUserData(userId, userData);
                    Debug.Log($"[TOMBSTONE] Subtraction complete: removed {removedCount} items from tombstone, remaining items={tombstone.items.Count}");
                    Debug.Log($"[TOMBSTONE] Final tombstone contains {tombstone.items.Count} droppable items");
                    
                    // 同步更新游戏中的墓碑Inventory
                    UpdateGameTombstoneInventory(lootUid, tombstone);
                }
                else
                {
                    Debug.LogWarning($"[TOMBSTONE] No items were subtracted from tombstone: userId={userId}, lootUid={lootUid}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to subtract remaining items from tombstone: {e}");
            }
        }

        /// <summary>
        /// 获取场景中的墓碑数据
        /// </summary>
        public List<TombstoneData> GetSceneTombstones(string userId, string sceneId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sceneId))
            {
                Debug.LogWarning($"[TOMBSTONE] GetSceneTombstones: invalid parameters - userId={userId}, sceneId={sceneId}");
                return new List<TombstoneData>();
            }

            try
            {
                Debug.Log($"[TOMBSTONE] Getting scene tombstones for userId={userId}, sceneId={sceneId}");
                var userData = LoadUserData(userId);
                var sceneTombstones = userData.tombstones.FindAll(t => t.sceneId == sceneId);
                
                Debug.Log($"[TOMBSTONE] Found {sceneTombstones.Count} tombstones for userId={userId}, sceneId={sceneId}");
                
                // 检查并清理空墓碑
                var emptyTombstones = new List<TombstoneData>();
                var validTombstones = new List<TombstoneData>();
                
                // 输出匹配的墓碑详细信息并分类
                for (int i = 0; i < sceneTombstones.Count; i++)
                {
                    var tombstone = sceneTombstones[i];
                    var totalItems = tombstone.items?.Count ?? 0;
                    Debug.Log($"[TOMBSTONE] Scene tombstone {i}: lootUid={tombstone.lootUid}, items={totalItems}, position={tombstone.position}");
                    
                    if (totalItems == 0)
                    {
                        Debug.LogWarning($"[TOMBSTONE] Found empty tombstone: userId={userId}, lootUid={tombstone.lootUid}, sceneId={sceneId}");
                        emptyTombstones.Add(tombstone);
                    }
                    else
                    {
                        // 统计物品类型
                        var itemTypes = new Dictionary<int, int>();
                        foreach (var item in tombstone.items)
                        {
                            if (itemTypes.ContainsKey(item.snapshot.typeId))
                                itemTypes[item.snapshot.typeId] += item.snapshot.stack;
                            else
                                itemTypes[item.snapshot.typeId] = item.snapshot.stack;
                        }
                        
                        Debug.Log($"[TOMBSTONE]   Item summary: {string.Join(", ", itemTypes.Select(kv => $"TypeID:{kv.Key}x{kv.Value}"))}");
                        validTombstones.Add(tombstone);
                    }
                }
                
                // 如果有空墓碑，从用户数据中移除并保存
                if (emptyTombstones.Count > 0)
                {
                    Debug.Log($"[TOMBSTONE] Removing {emptyTombstones.Count} empty tombstones for userId={userId}");
                    
                    // 从用户数据中移除空墓碑
                    foreach (var emptyTombstone in emptyTombstones)
                    {
                        userData.tombstones.RemoveAll(t => t.lootUid == emptyTombstone.lootUid);
                        Debug.Log($"[TOMBSTONE] Removed empty tombstone: lootUid={emptyTombstone.lootUid}");
                    }
                    
                    // 检查用户是否还有其他墓碑
                    if (userData.tombstones.Count == 0)
                    {
                        // 如果用户没有任何墓碑了，删除整个JSON文件
                        var filePath = GetUserDataPath(userId);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            Debug.Log($"[TOMBSTONE] Deleted empty tombstone file: {filePath}");
                        }
                        
                        // 从缓存中移除
                        if (_userDataCache.ContainsKey(userId))
                        {
                            _userDataCache.Remove(userId);
                            Debug.Log($"[TOMBSTONE] Removed user data from cache: {userId}");
                        }
                    }
                    else
                    {
                        // 还有其他墓碑，保存更新后的数据
                        SaveUserData(userId, userData);
                        Debug.Log($"[TOMBSTONE] Updated tombstone file after removing empty tombstones: userId={userId}, remaining tombstones={userData.tombstones.Count}");
                    }
                }
                
                return validTombstones;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to get scene tombstones: {e}");
                return new List<TombstoneData>();
            }
        }

        /// <summary>
        /// 获取特定墓碑数据
        /// </summary>
        public TombstoneData GetTombstone(string userId, int lootUid)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            try
            {
                var userData = LoadUserData(userId);
                return userData.tombstones.Find(t => t.lootUid == lootUid);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to get tombstone: {e}");
                return null;
            }
        }

        /// <summary>
        /// 更新墓碑物品数据（当物品被拾取时）
        /// </summary>
        public void UpdateTombstoneItems(string userId, int lootUid, Inventory inventory)
        {
            if (!IsServer || string.IsNullOrEmpty(userId))
            {
                return;
            }

            try
            {
                var fileName = GetUserDataPath(userId);
                Debug.Log($"[TOMBSTONE] UpdateTombstoneItems - userId={userId}, lootUid={lootUid}, fileName={fileName}");
                
                var userData = LoadUserData(userId);
                Debug.Log($"[TOMBSTONE] Loaded user data: userId={userId}, tombstones count={userData.tombstones.Count}");
                
                var tombstone = userData.tombstones.Find(t => t.lootUid == lootUid);
                
                if (tombstone == null)
                {
                    Debug.LogWarning($"[TOMBSTONE] Tombstone not found for update: userId={userId}, lootUid={lootUid}, fileName={fileName}");
                    Debug.Log($"[TOMBSTONE] Available tombstones for user {userId}:");
                    for (int i = 0; i < userData.tombstones.Count; i++)
                    {
                        var t = userData.tombstones[i];
                        Debug.Log($"[TOMBSTONE]   Tombstone {i}: lootUid={t.lootUid}, sceneId={t.sceneId}, items={t.items.Count}");
                    }
                    return;
                }

                // 清空现有物品数据
                tombstone.items.Clear();

                // 重新序列化当前物品
                if (inventory != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        var item = inventory.GetItemAt(i);
                        if (item != null)
                        {
                            try
                            {
                                var snapshot = ItemTool.MakeSnapshot(item);
                                tombstone.items.Add(new TombstoneItem
                                {
                                    position = i,
                                    snapshot = snapshot
                                });
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[TOMBSTONE] Failed to serialize item at position {i}: {e}");
                            }
                        }
                    }
                }

                SaveUserData(userId, userData);
                Debug.Log($"[TOMBSTONE] Updated tombstone items: userId={userId}, lootUid={lootUid}, items={tombstone.items.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to update tombstone items: {e}");
            }
        }

        /// <summary>
        /// 删除墓碑数据
        /// </summary>
        public void RemoveTombstone(string userId, int lootUid)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            try
            {
                var userData = LoadUserData(userId);
                var removed = userData.tombstones.RemoveAll(t => t.lootUid == lootUid);
                
                if (removed > 0)
                {
                    SaveUserData(userId, userData);
                    Debug.Log($"[TOMBSTONE] Removed {removed} tombstone(s): userId={userId}, lootUid={lootUid}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to remove tombstone: {e}");
            }
        }

        /// <summary>
        /// 清理过期墓碑（可选功能，比如30天后自动清理）
        /// </summary>
        public void CleanupExpiredTombstones(string userId, long maxAgeSeconds = 30 * 24 * 60 * 60) // 默认30天
        {
            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            try
            {
                var userData = LoadUserData(userId);
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var initialCount = userData.tombstones.Count;
                
                userData.tombstones.RemoveAll(t => (currentTime - t.createTime) > maxAgeSeconds);
                
                var removedCount = initialCount - userData.tombstones.Count;
                if (removedCount > 0)
                {
                    SaveUserData(userId, userData);
                    Debug.Log($"[TOMBSTONE] Cleaned up {removedCount} expired tombstones for userId={userId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to cleanup expired tombstones: {e}");
            }
        }

        /// <summary>
        /// 获取用户ID（从网络服务或其他地方获取）
        /// </summary>
        public string GetUserIdFromPeer(NetPeer peer)
        {
            try
            {
                if (Service?.playerStatuses?.TryGetValue(peer, out var status) == true)
                {
                    return status.EndPoint; // 使用EndPoint作为用户ID
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to get userId from peer: {e}");
            }
            
            return peer?.EndPoint?.ToString() ?? "unknown";
        }

        /// <summary>
        /// 获取本地用户ID
        /// </summary>
        public string GetLocalUserId()
        {
            try
            {
                if (Service?.localPlayerStatus != null)
                {
                    return Service.localPlayerStatus.EndPoint;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to get local userId: {e}");
            }
            
            return "local_player";
        }

        /// <summary>
        /// 通过lootUid更新墓碑数据，自动查找对应的用户
        /// </summary>
        public void UpdateTombstoneByLootUid(int lootUid, Inventory inventory)
        {
            if (!IsServer || lootUid < 0)
            {
                return;
            }

            try
            {
                Debug.Log($"[TOMBSTONE] UpdateTombstoneByLootUid called with lootUid: {lootUid}");
                
                // 扫描所有用户的墓碑文件，找到包含这个lootUid的用户
                var tombstoneDir = _streamingAssetsPath;
                if (!Directory.Exists(tombstoneDir))
                {
                    Debug.LogWarning($"[TOMBSTONE] Tombstone directory does not exist: {tombstoneDir}");
                    return;
                }

                var files = Directory.GetFiles(tombstoneDir, "*_tombstones.json");
                Debug.Log($"[TOMBSTONE] Scanning {files.Length} tombstone files for lootUid {lootUid}");

                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var userId = DecodeUserIdFromFileName(fileName);
                        
                        if (string.IsNullOrEmpty(userId)) continue;

                        var userData = LoadUserData(userId);
                        var tombstone = userData.tombstones.Find(t => t.lootUid == lootUid);
                        
                        if (tombstone != null)
                        {
                            Debug.Log($"[TOMBSTONE] Found tombstone with lootUid {lootUid} in user {userId}'s file: {fileName}");
                            
                            // 更新这个墓碑的物品数据
                            UpdateTombstoneItems(userId, lootUid, inventory);
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TOMBSTONE] Failed to process file {file}: {e}");
                    }
                }

                Debug.LogWarning($"[TOMBSTONE] No tombstone found with lootUid: {lootUid}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to update tombstone by lootUid: {e}");
            }
        }

        /// <summary>
        /// 同步更新游戏中的墓碑Inventory，使其与JSON数据一致
        /// </summary>
        private void UpdateGameTombstoneInventory(int lootUid, TombstoneData tombstoneData)
        {
            if (!IsServer || LootManager.Instance == null)
            {
                return;
            }

            try
            {
                Debug.Log($"[TOMBSTONE] Updating game tombstone inventory: lootUid={lootUid}");
                
                // 从LootManager中找到对应的Inventory
                var lootManager = LootManager.Instance;
                if (!lootManager._srvLootByUid.TryGetValue(lootUid, out var inventory) || inventory == null)
                {
                    Debug.LogWarning($"[TOMBSTONE] Game tombstone inventory not found: lootUid={lootUid}");
                    return;
                }

                Debug.Log($"[TOMBSTONE] Found game tombstone inventory with {inventory.Content.Count} items");
                
                // 设置标志阻止物品掉落和其他副作用
                COOPManager.LootNet._serverApplyingLoot = true;
                
                try
                {
                    // 清空当前库存 - 逐个移除所有物品
                    var itemCount = inventory.Content.Count;
                    for (int i = itemCount - 1; i >= 0; i--)
                    {
                        var item = inventory.GetItemAt(i);
                        if (item != null)
                        {
                            inventory.RemoveAt(i, out _);
                        }
                    }
                    
                    // 根据JSON数据重建库存
                    var rebuiltCount = 0;
                    foreach (var tombstoneItem in tombstoneData.items)
                    {
                        try
                        {
                            var item = ItemTool.BuildItemFromSnapshot(tombstoneItem.snapshot);
                            if (item != null)
                            {
                                inventory.AddAt(item, tombstoneItem.position);
                                rebuiltCount++;
                                Debug.Log($"[TOMBSTONE] Rebuilt item in game inventory: TypeID={item.TypeID}, Name={item.name}, Position={tombstoneItem.position}");
                            }
                            else
                            {
                                Debug.LogWarning($"[TOMBSTONE] Failed to rebuild item from snapshot: TypeID={tombstoneItem.snapshot.typeId}");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[TOMBSTONE] Error rebuilding item at position {tombstoneItem.position}: {e}");
                        }
                    }
                    
                    Debug.Log($"[TOMBSTONE] Game tombstone inventory updated: {rebuiltCount} items rebuilt");
                }
                finally
                {
                    // 确保标志被重置
                    COOPManager.LootNet._serverApplyingLoot = false;
                }
                
                // 广播更新给所有客户端
                if (COOPManager.LootNet != null)
                {
                    Debug.Log($"[TOMBSTONE] Broadcasting updated tombstone state to all clients: lootUid={lootUid}");
                    COOPManager.LootNet.Server_SendLootboxState(null, inventory);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TOMBSTONE] Failed to update game tombstone inventory: {e}");
            }
        }
    }
}