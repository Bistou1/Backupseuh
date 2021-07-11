using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// BistouGames ♫
// On s'approprit Plant pour regrow le gazon from stage 1 au lieu de 0 pis toute les autre shit de plantes :D
// 18/mai [Tooltip("")]
// adding spawner script stuff but plant specific
// adding itemprovider stuff to have more than one fruit in the plant
// 23/mai
namespace SurvivalEngine
{
    public enum PlantType
    { 
        Fruits,
        Bushes,
        Veggies,
        Tubercules,
        Spores,
        Grain,
    }

    /// <summary>
    /// Plants can be sowed (from a seed) and their fruit can be harvested. They can also have multiple growth stages.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    [RequireComponent(typeof(Buildable))]
    [RequireComponent(typeof(UniqueID))]
    [RequireComponent(typeof(Destructible))]
    public class Plant : Craftable
    {
        [Header("~~~Plant~~~")]
        public PlantData data;
        [Tooltip("The type of plant, for growing and reporduction purposes")]
        public PlantType plantType;
        [Tooltip("The stage of this prefab")]
        public int growth_stage = 0;

        [Header("~~~Growth~~~")]
        [Tooltip("In-game hours")]
        public float grow_time = 8f;
        [Tooltip("In percentage (0f = no difference, 0.5f = 50% boost)")]
        public float water_grow_boost = 1f;
        [Tooltip("In-game hours, how long it stays wet")]
        public float water_duration = 4f;
        [Tooltip("If true, will go back to stage 1 instead of being destroyed")]
        public bool regrow_on_death;
        [Tooltip("If regrows, go back to this stage")]
        public int regrowStage = 0;

        [Header("~~~Offspring~~~")]
        [Tooltip("In-game hours")]
        public float spawnInterval = 0f;
        [Tooltip("Circle radius of the spawn zone, keep it big enough so it can keep track of the already spawned ones.")]
        public float spawnRadius = 10f;
        [Tooltip("If there are more than this already in the radius, will stop spawning.")]
        public int maxAmount = 1;
        [Tooltip("Floor that this can be spawned on")]
        public LayerMask validFloorLayer = (1 << 9);
        [Tooltip("What stage does it starts from")]
        public int babyStage = 0;

        [Header("~~~Flower~~~")]
        [Tooltip("Which data it is")]
        public ItemData flower;
        [Tooltip("How long for the flowers to grow and be ready, in-game hour")]
        public float flowerGrowTime = 0f;
        [Tooltip("The amount of time the flower stays ready")]
        public float flowerLifeTime = 0f;
        [Tooltip("Where the flowers are placed")]
        public GameObject[] flowerModels;
        [Tooltip("Has it been pollinated while the flower was ready")]
        public bool pollinated;

        [Header("~~~Fruit~~~")]
        [Tooltip("Which fruit data it is")]
        public ItemData fruit;
        [Tooltip("How long to grow one fruit, in-game hour")]
        public float fruit_grow_time = 0f;
        [Tooltip("Maximum amount of fruit at once")]
        public float fruitMax = 1f;
        [Tooltip("Where the fruits are placed how many")]
        public GameObject[] fruitModels;
        [Tooltip("Is harvesting it fatal")]
        public bool death_on_harvest;

        [Header("FX")]
        public GameObject gather_fx;
        public AudioClip gather_audio;

        [HideInInspector]
        public bool was_spawned = false; //If true, means it was crafted or loaded from save file

        private Selectable selectable;
        private Buildable buildable;
        private Destructible destruct;
        private UniqueID unique_id;

        private int nb_stages = 1;

        private int nbFruit = 0;
        [Tooltip("runtime fruit growth")]
        private float fruit_progress = 0f;
        private float growth_progress = 0f;

        [Tooltip("For the offspring")]
        private float spawnTimer = 0f;

        private float boost_mult = 1f;
        [Tooltip("The water duration timer")]
        private float boost_timer = 0f;

        private static List<Plant> plant_list = new List<Plant>();

        protected override void Awake()
        {
            base.Awake();
            plant_list.Add(this);
            selectable = GetComponent<Selectable>();
            buildable = GetComponent<Buildable>();
            destruct = GetComponent<Destructible>();
            unique_id = GetComponent<UniqueID>();
            selectable.onDestroy += OnDeath;
            buildable.onBuild += OnBuild;

            if(data != null)
                nb_stages = Mathf.Max(data.growth_stage_prefabs.Length, 1);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            plant_list.Remove(this);
        }

        void Start()
        {
            if (!was_spawned && PlayerData.Get().IsObjectRemoved(GetUID()))
            {
                Destroy(gameObject);
                return;
            }


            for (int i = 0; i < fruitModels.Length; i++)
            {
                if (fruitModels != null)
                    fruitModels[i].SetActive(false);
            }

            // Offspring timer
            if (PlayerData.Get().HasCustomFloat(GetSpawnTimerUID()))
                spawnTimer = PlayerData.Get().GetCustomFloat(GetSpawnTimerUID());

            //Fruit
            //if (PlayerData.Get().HasCustomInt(GetSubUID("fruit")))
            //    has_fruit = PlayerData.Get().GetCustomInt(GetSubUID("fruit")) > 0;

            if (PlayerData.Get().HasCustomInt(GetAmountUID()))
                nbFruit = PlayerData.Get().GetCustomInt(GetAmountUID());

            //Progress
            if (PlayerData.Get().HasCustomFloat(GetSubUID("progress")))
            {
                growth_progress = PlayerData.Get().GetCustomFloat(GetSubUID("progress"));
                fruit_progress = PlayerData.Get().GetCustomFloat(GetSubUID("progress"));
            }
        }

        void Update()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (buildable.IsBuilding())
                return;

            float game_speed = TheGame.Get().GetGameTimeSpeedPerSec();

            // y reste des stage pis ya un grow time
            if (!IsFullyGrown() && grow_time > 0.001f)
            {
                // grow progress monte et save
                growth_progress += game_speed * boost_mult * Time.deltaTime;
                PlayerData.Get().SetCustomFloat(GetSubUID("progress"), growth_progress);

                // si sa depasse grow time, sa grow
                if (growth_progress > grow_time)
                {
                    GrowPlant();
                    return;
                }
            }

            // Spawn offspring
            if (IsFullyGrown() && spawnInterval > 0.001 && !IsFull())
            {
                spawnTimer += game_speed * Time.deltaTime;
                PlayerData.Get().SetCustomFloat(GetSpawnTimerUID(), spawnTimer);

                if (spawnTimer > spawnInterval)
                {
                    spawnTimer = 0f;
                    SpawnOffspring();
                }
            }

            // add flower stuff ♪

            // si ya pas plein de fruit mais que ya un itemData dans fruit
            if (!FullFruit() && fruit != null)
            {
                // progress monte et save
                fruit_progress += game_speed * boost_mult * Time.deltaTime;
                PlayerData.Get().SetCustomFloat(GetSubUID("progress"), fruit_progress);

                // si sa depasse grow time, sa grow
                if (fruit_progress > fruit_grow_time)
                {
                    GrowFruit();
                    return;
                }
            }

            //Boost stop
            if (boost_timer > 0f)
            {
                boost_timer -= game_speed * Time.deltaTime;
                if (boost_timer <= 0.01f)
                    boost_mult = 1f;
            }

            //Water 
            if (!HasWater() && TheGame.Get().IsWeather(WeatherEffect.Rain))
                Water();

            //Display
            for (int i = 0; i < fruitModels.Length; i++)
            {
                bool visible = (i < nbFruit);
                if (fruitModels[i].activeSelf != visible)
                    fruitModels[i].SetActive(visible);
            }
        }

        public void GrowPlant()
        {
            if (!IsFullyGrown())
            {
                GrowPlant(growth_stage + 1);
            }
        }

        public void GrowPlant(int grow_stage)
        {
            if (data != null && growth_stage >= 0 && growth_stage < nb_stages)
            {
                SowedPlantData sdata = PlayerData.Get().GetSowedPlant(GetUID());
                if (sdata == null)
                {
                    //Remove this plant and create a new one (this one probably was already in the scene)
                    if (!was_spawned)
                        PlayerData.Get().RemoveObject(GetUID()); //Remove Unique id
                    sdata = PlayerData.Get().AddPlant(data.id, SceneNav.GetCurrentScene(), transform.position, transform.rotation, grow_stage);
                }
                else
                {
                    //Grow current plant from data
                    PlayerData.Get().GrowPlant(GetUID(), grow_stage);
                }

                growth_progress = 0f;
                PlayerData.Get().SetCustomFloat(GetSubUID("progress"), 0f);
                plant_list.Remove(this); //Remove from list so spawn works!

                Spawn(sdata.uid);
                Destroy(gameObject);
            }
        }

        public void GrowFruit()
        {
            fruit_progress = 0f;
            nbFruit += 1;
            PlayerData.Get().SetCustomInt(GetSubUID("fruit"), nbFruit);
            PlayerData.Get().SetCustomFloat(GetSubUID("progress"), 0f);
        }

        public void SpawnOffspring()
        {
            if (data != null && IsPollenSeason())
            {
                float radius = Random.Range(0f, spawnRadius);
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 pos = transform.position + offset;
                Vector3 ground_pos;
                bool found = PhysicsTool.FindGroundPosition(pos, 100f, validFloorLayer.value, out ground_pos);

                if (found)
                {
                    Create(data, pos, regrowStage);
                }
            }
        }

        public void Water()
        {
            boost_mult = (1f + water_grow_boost);
            boost_timer = water_duration;
        }

        /// <summary>
        /// Harvest the fruit, possibly killing the plant. With Fx and audio
        /// </summary>
        /// <param name="character"></param>
        public void Harvest(PlayerCharacter character)
        {
            // action harvest
            if (fruit != null && HasFruit() && character.Inventory.CanTakeItem(fruit, 1))
            {
                // si on dit juste le transform direct au lieu de source sa fait quoi ?
                //GameObject source = fruit_model != null ? fruit_model.gameObject : gameObject;
                character.Inventory.GainItem(fruit, 1, /*source.*/transform.position);

                RemoveFruit();

                // if the plant is gone after harvest, like carrots, unlike tomatoes
                if (death_on_harvest && destruct != null)
                    destruct.Kill();

                TheAudio.Get().PlaySFX("plant", gather_audio);

                if (gather_fx != null)
                    Instantiate(gather_fx, transform.position, Quaternion.identity);
            }
        }

        /// <summary>
        /// Removes fruit and save
        /// </summary>
        public void RemoveFruit()
        {
            nbFruit--;

            PlayerData.Get().SetCustomInt(GetSubUID("fruit"), nbFruit);
        }

        /// <summary>
        /// Calls destructible.kill which already calls the loot
        /// </summary>
        public void Kill()
        {
            destruct.Kill();
        }

        public void KillNoLoot()
        {
            destruct.KillNoLoot(); //Such as when being eaten, dont spawn loot
        }

        /// <summary>
        /// To store on the OnBuild UnityAction
        /// </summary>
        private void OnBuild()
        {
            if (data != null)
            {
                SowedPlantData splant = PlayerData.Get().AddPlant(data.id, SceneNav.GetCurrentScene(), transform.position, transform.rotation, growth_stage);
                unique_id.unique_id = splant.uid;
            }
        }

        /// <summary>
        /// When the selectable is destroyed
        /// </summary>
        private void OnDeath()
        {
            if (data != null)
            {
                foreach (PlayerCharacter character in PlayerCharacter.GetAll())
                    character.Data.AddKillCount(data.id); //Add kill count
            }

            PlayerData.Get().RemovePlant(GetUID());
            if (!was_spawned)
                PlayerData.Get().RemoveObject(GetUID());

            //drops its fruits on death
            // la y va juste drop un fruit avec la bonne 
            if (HasFruit())
                DropFruits();

            if (data != null && regrow_on_death)
            {
                SowedPlantData sdata = PlayerData.Get().GetSowedPlant(GetUID());
                Create(data, transform.position, transform.rotation, regrowStage);
            }
        }

        private void DropFruits()
        {

            for (int i = 0; i < fruitModels.Length; i++)
            {
                //in the loop so each has a different pos
                Vector3 pos = destruct.GetLootRandomPos();

                // not sure, might not be the way =/
                bool visible = (i < nbFruit);
                if (fruitModels[i].activeSelf == visible)
                {
                    Item.Create(fruit, pos, fruitModels.Length);
                }
            }                
        }

        #region Refs

        /// <summary>
        /// Le bool HasFruit se fait remplacer par this
        /// </summary>
        /// <returns>nbFruit > 0</returns>
        public bool HasFruit()
        {
            //if (nbFruit > 0)
            //    has_fruit = true;

            //return has_fruit;

            return nbFruit > 0;
        }

        /// <summary>
        /// When the plant has maxed its fruit amount
        /// </summary>
        /// <returns>nbFruit >= fruitMax</returns>
        public bool FullFruit()
        {
            return nbFruit >= fruitMax;
        }

        /// <summary>
        /// Only sends seeds in right season, but no seasons yet ♪
        /// </summary>
        /// <returns></returns>
        public bool IsPollenSeason()
        {
            return true;
        }

        public bool HasWater()
        {
            return boost_timer > 0f;
        }

        public bool IsFullyGrown()
        {
            // +1 because it starts at 0
            return (growth_stage + 1) >= nb_stages;
        }

        /// <summary>
        /// Alive and finished building
        /// </summary>
        /// <returns></returns>
        public bool IsBuilt()
        {
            return !IsDead() && !buildable.IsBuilding();
        }

        public bool IsDead()
        {
            return destruct.IsDead();
        }
        
        public bool HasUID()
        {
            return !string.IsNullOrEmpty(unique_id.unique_id);
        }

        public string GetUID()
        {
            return unique_id.unique_id;
        }

        public string GetSubUID(string tag)
        {
            return unique_id.GetSubUID(tag);
        }

        public bool HasGroup(GroupData group)
        {
            if (data != null)
                return data.HasGroup(group) || selectable.HasGroup(group);
            return selectable.HasGroup(group);
        }

        public Selectable GetSelectable()
        {
            return selectable;
        }

        public Destructible GetDestructible()
        {
            return destruct;
        }

        public Buildable GetBuildable()
        {
            return buildable;
        }

        public SowedPlantData SaveData
        {
            get { return PlayerData.Get().GetSowedPlant(GetUID()); }  //Can be null if not sowed or spawned
        }
        #endregion

        public static new Plant GetNearest(Vector3 pos, float range = 999f)
        {
            Plant nearest = null;
            float min_dist = range;
            foreach (Plant plant in plant_list)
            {
                float dist = (plant.transform.position - pos).magnitude;
                if (dist < min_dist && plant.IsBuilt())
                {
                    min_dist = dist;
                    nearest = plant;
                }
            }
            return nearest;
        }

        public static int CountInRange(Vector3 pos, float range)
        {
            int count = 0;
            foreach (Plant plant in GetAll())
            {
                float dist = (plant.transform.position - pos).magnitude;
                if (dist < range && plant.IsBuilt())
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Check if there is already enough of this plant
        /// </summary>
        /// <returns></returns>
        public bool IsFull()
        {
            return CountAllOfInRange(this.data, transform.position, spawnRadius) >= maxAmount;
        }

        /// <summary>
        /// Amongst the corresponding plant, how many are in range
        /// </summary>
        /// <param name="data">Plant to check</param>
        /// <param name="pos">Position of that plant</param>
        /// <param name="range">Range to check</param>
        /// <returns></returns>
        public int CountAllOfInRange(PlantData data, Vector3 pos, float range)
        {
            int count = 0;
            foreach (Plant plant in GetAllOf(data))
            {
                if (plant.data == data && plant.IsBuilt())
                {
                    float dist = (plant.transform.position - pos).magnitude;
                    if (dist < range)
                        count++;
                }
            }
            return count;
        }

        public static int CountInRange(PlantData data, Vector3 pos, float range)
        {
            int count = 0;
            foreach (Plant plant in GetAll())
            {
                if (plant.data == data && plant.IsBuilt())
                {
                    float dist = (plant.transform.position - pos).magnitude;
                    if (dist < range)
                        count++;
                }
            }
            return count;
        }

        public static Plant GetByUID(string uid)
        {
            if (!string.IsNullOrEmpty(uid))
            {
                foreach (Plant plant in plant_list)
                {
                    if (plant.GetUID() == uid)
                        return plant;
                }
            }
            return null;
        }

        /// <summary>
        /// Only counts the plant that fit the given data
        /// </summary>
        /// <param name="data"></param>
        /// <returns>All of one type of plant</returns>
        public static List<Plant> GetAllOf(PlantData data)
        {
            List<Plant> valid_list = new List<Plant>();
            foreach (Plant plant in plant_list)
            {
                if (plant.data == data)
                    valid_list.Add(plant);
            }
            return valid_list;
        }

        public static new List<Plant> GetAll()
        {
            return plant_list;
        }

        /// <summary>
        /// Spawn an existing one in the save file (such as after loading)
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static Plant Spawn(string uid, Transform parent = null)
        {
            SowedPlantData sdata = PlayerData.Get().GetSowedPlant(uid);
            if (sdata != null && sdata.scene == SceneNav.GetCurrentScene())
            {
                PlantData pdata = PlantData.Get(sdata.plant_id);
                if (pdata != null)
                {
                    GameObject prefab = pdata.GetStagePrefab(sdata.growth_stage);
                    GameObject build = Instantiate(prefab, sdata.pos, sdata.rot);
                    build.transform.parent = parent;

                    Plant plant = build.GetComponent<Plant>();
                    plant.data = pdata;
                    plant.growth_stage = sdata.growth_stage;
                    plant.was_spawned = true;
                    plant.unique_id.unique_id = uid;
                    return plant;
                }
            }
            return null;
        }

        /// <summary>
        /// Create a totally new one, in build mode for player to place, will be saved after FinishBuild() is called, -1 = max stage
        /// </summary>
        /// <param name="data"></param>
        /// <param name="pos"></param>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static Plant CreateBuildMode(PlantData data, Vector3 pos, int stage)
        {
            GameObject prefab = data.GetStagePrefab(stage);
            GameObject build = Instantiate(prefab, pos, prefab.transform.rotation);
            Plant plant = build.GetComponent<Plant>();
            plant.data = data;
            plant.was_spawned = true;

            if(stage >= 0 && stage < data.growth_stage_prefabs.Length)
                plant.growth_stage = stage;
            
            return plant;
        }

        /// <summary>
        /// Create a totally new one that will be added to save file, already placed
        /// </summary>
        /// <param name="data"></param>
        /// <param name="pos"></param>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static Plant Create(PlantData data, Vector3 pos, int stage)
        {
            Plant plant = CreateBuildMode(data, pos, stage);
            plant.buildable.FinishBuild();
            return plant;
        }

        /// <summary>
        /// Create the regrown plant
        /// </summary>
        /// <param name="data"></param>
        /// <param name="pos"></param>
        /// <param name="rot"></param>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static Plant Create(PlantData data, Vector3 pos, Quaternion rot, int stage)
        {
            Plant plant = CreateBuildMode(data, pos, stage);
            plant.transform.rotation = rot;
            plant.buildable.FinishBuild();
            return plant;
        }

        public string GetSpawnTimerUID()
        {
            if (unique_id != null && !string.IsNullOrEmpty(unique_id.unique_id))
                return unique_id.unique_id + "_timer";
            return "";
        }

        public string GetAmountUID()
        {
            if (!string.IsNullOrEmpty(unique_id.unique_id))
                return unique_id.unique_id + "_amount";
            return "";
        }
    }

}