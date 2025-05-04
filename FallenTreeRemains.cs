/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Fallen Tree Remains", "VisEntities", "1.2.0")]
    [Description("Leaves a dead log and stump after a tree is cut down, which can also be harvested.")]
    public class FallenTreeRemains : RustPlugin
    {
        #region Fields

        private static FallenTreeRemains _plugin;
        private static Configuration _config;

        private const int LAYER_GROUND = Layers.Mask.World | Layers.Mask.Terrain;
        
        private const string PREFAB_STUMP = "assets/bundled/prefabs/autospawn/collectable/wood/wood-collectable.prefab";
        private const string PREFAB_JUNGLE_STUMP = "assets/content/nature/treessource/kapok_tree/vineswingingtreestump.prefab";
        private const string PREFAB_WOOD_PILE = "assets/bundled/prefabs/autospawn/resource/wood_log_pile/wood-pile.prefab";
        private static readonly string[] _dryDeadLogPrefabs = new[]
        {
            "assets/bundled/prefabs/autospawn/resource/logs_dry/dead_log_a.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_dry/dead_log_b.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_dry/dead_log_c.prefab"
        };
        private static readonly string[] _wetDeadLogPrefabs = new[]
        {
            "assets/bundled/prefabs/autospawn/resource/logs_wet/dead_log_a.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_wet/dead_log_b.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_wet/dead_log_c.prefab"
        };
        private static readonly string[] _snowDeadLogPrefabs = new[]
        {
            "assets/bundled/prefabs/autospawn/resource/logs_snow/dead_log_a.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_snow/dead_log_b.prefab",
            "assets/bundled/prefabs/autospawn/resource/logs_snow/dead_log_c.prefab"
        };

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Chance To Spawn Snow Log")]
            public int ChanceToSpawnSnowLog { get; set; }

            [JsonProperty("Chance To Spawn Dry Log")]
            public int ChanceToSpawnDryLog { get; set; }

            [JsonProperty("Chance To Spawn Wet Log")]
            public int ChanceToSpawnWetLog { get; set; }

            [JsonProperty("Chance To Spawn Stump")]
            public int ChanceToSpawnStump { get; set; }

            [JsonProperty("Spawn Wood Pile Instead Of Stump")] 
            public bool SpawnWoodPileInsteadOfStump { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;


            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                _config.SpawnWoodPileInsteadOfStump = defaultConfig.SpawnWoodPileInsteadOfStump;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                ChanceToSpawnStump = 100,
                ChanceToSpawnDryLog = 50,
                ChanceToSpawnSnowLog = 50,
                ChanceToSpawnWetLog = 50,
                SpawnWoodPileInsteadOfStump = false
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (player == null)
                return;

            TreeEntity tree = dispenser.GetComponentInParent<TreeEntity>();
            if (tree != null)
            {
                timer.Once(tree.fallDuration + 0.7f, () =>
                {
                    SpawnTreeRemains(tree, player);
                });
            }
        }

        #endregion Oxide Hooks

        #region Tree Remains Spawning

        private void SpawnTreeRemains(TreeEntity tree, BasePlayer player)
        {
            if (tree == null || player == null)
                return;

            if (!tree.PrefabName.Contains("arid") && ChanceSucceeded(_config.ChanceToSpawnStump))
            {
                if (TerrainUtil.GetGroundInfo(tree.transform.position, out RaycastHit raycastHit, 5f, LAYER_GROUND))
                {
                    string stumpPrefab;
                    if (_config.SpawnWoodPileInsteadOfStump)
                        stumpPrefab = PREFAB_WOOD_PILE;
                    else
                    {
                        if (tree.PrefabName.Contains("vine_swinging"))
                            stumpPrefab = PREFAB_JUNGLE_STUMP;
                        else
                            stumpPrefab = PREFAB_STUMP;
                    }

                    BaseEntity entity = SpawnStump(stumpPrefab, raycastHit.point, Quaternion.FromToRotation(Vector3.up, raycastHit.normal));
                }
            }

            string deadLogPrefab = GetDeadLogPrefabForTree(tree);
            if (deadLogPrefab != null)
            {
                ResourceEntity deadLog = SpawnDeadLog(deadLogPrefab, tree, player.eyes.HeadRay().direction);
            }
        }

        private BaseEntity SpawnStump(string prefabPath, Vector3 position, Quaternion rotation, bool wakeUpNow = true)
        {
            BaseEntity stump = GameManager.server.CreateEntity(prefabPath, position, rotation, wakeUpNow);
            if (stump == null)
                return null;

            stump.Spawn();
            return stump;
        }

        private ResourceEntity SpawnDeadLog(string prefabPath, TreeEntity tree, Vector3 fallDirection, bool wakeUpNow = true)
        {
            ResourceEntity deadLog = GameManager.server.CreateEntity(prefabPath, tree.transform.position, Quaternion.identity, wakeUpNow) as ResourceEntity;
            if (deadLog == null)
                return null;

            deadLog.Spawn();

            OBB bounds = deadLog.WorldSpaceBounds();

            Vector3 leftmostPoint = bounds.position + bounds.rotation * new Vector3(-bounds.extents.x, 0, 0);
            Vector3 rightmostPoint = bounds.position + bounds.rotation * new Vector3(bounds.extents.x, 0, 0);
            float length = Vector3.Distance(leftmostPoint, rightmostPoint);

            deadLog.transform.position = tree.transform.position + (fallDirection.normalized * (length / 2));
            deadLog.transform.rotation = Quaternion.Euler(0, 90f, 0) * Quaternion.LookRotation(fallDirection);
            deadLog.SendNetworkUpdateImmediate();

            if (!TerrainUtil.GetGroundInfo(deadLog.transform.position, out RaycastHit raycastHit, 5f, LAYER_GROUND))
            {
                deadLog.Kill();
                return null;
            }

            deadLog.transform.position = raycastHit.point;
            deadLog.transform.rotation = Quaternion.FromToRotation(Vector3.up, raycastHit.normal) * deadLog.transform.rotation;
            deadLog.SendNetworkUpdateImmediate();

            return deadLog;
        }

        private string GetDeadLogPrefabForTree(TreeEntity tree)
        {
            if ((tree.PrefabName.Contains("tundra") || tree.PrefabName.Contains("temp")) && ChanceSucceeded(_config.ChanceToSpawnDryLog))
            {
                return _dryDeadLogPrefabs[Random.Range(0, _dryDeadLogPrefabs.Length)];
            }
            else if (tree.PrefabName.Contains("arctic") && ChanceSucceeded(_config.ChanceToSpawnSnowLog))
            {
                return _snowDeadLogPrefabs[Random.Range(0, _snowDeadLogPrefabs.Length)];
            }
            else if (tree.PrefabName.Contains("swamp") && ChanceSucceeded(_config.ChanceToSpawnWetLog))
            {
                return _wetDeadLogPrefabs[Random.Range(0, _wetDeadLogPrefabs.Length)];
            }
            else if ((tree.PrefabName.Contains("jungle") || tree.PrefabName.Contains("vine_swinging"))
                && ChanceSucceeded(_config.ChanceToSpawnDryLog))
            {
                return _dryDeadLogPrefabs[Random.Range(0, _dryDeadLogPrefabs.Length)];
            }
            else if (tree.PrefabName.Contains("arid"))
            {
                return null;
            }

            return null;
        }

        #endregion Tree Remains Spawning

        #region Helper Functions

        private static bool ChanceSucceeded(int percent)
        {
            if (percent <= 0)
                return false;

            if (percent >= 100)
                return true;

            float roll = Random.Range(0f, 100f);
            return roll < percent;
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class TerrainUtil
        {
            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask)
            {
                return Physics.Linecast(startPosition + new Vector3(0.0f, range, 0.0f), startPosition - new Vector3(0.0f, range, 0.0f), out raycastHit, mask);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask, Transform ignoreTransform = null)
            {
                startPosition.y += 0.25f;
                range += 0.25f;
                raycastHit = default;

                RaycastHit hit;
                if (!GamePhysics.Trace(new Ray(startPosition, Vector3.down), 0f, out hit, range, mask, QueryTriggerInteraction.UseGlobal, null))
                    return false;

                if (ignoreTransform != null && hit.collider != null
                    && (hit.collider.transform == ignoreTransform || hit.collider.transform.IsChildOf(ignoreTransform)))
                {
                    return GetGroundInfo(startPosition - new Vector3(0f, 0.01f, 0f), out raycastHit, range, mask, ignoreTransform);
                }

                raycastHit = hit;
                return true;
            }
        }
        
        #endregion Helper Classes
    }
}