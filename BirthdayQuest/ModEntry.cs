using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.Menus;
using StardewValley.GameData.SpecialOrders;
using StardewValley.SpecialOrders;
using StardewValley.GameData.Objects;
using StardewValley.TokenizableStrings;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using System.Linq.Expressions;

namespace BirthdayQuest
{
    /// <summary>The mod entry point.</summary>
    internal sealed class MyMod : Mod
    {
        private ModConfig Config = new();

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        
        public override void Entry(IModHelper helper)
        {
            this.Config = this.Helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;

            helper.Events.GameLoop.DayStarted += this.OnDayStarted;

            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;

            helper.Events.Display.MenuChanged += this.OnClosedMenu;

            helper.Events.Content.AssetRequested += this.OnAssetRequested;

        }

        /*********
        ** Private methods
        *********/

        /*********
        ** GMCM supports
        *********/

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

            if (configMenu is null)
            {
                return;
            }

            // register mod
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    this.Helper.GameContent.InvalidateCache("Data/SpecialOrders");
                }
            );

            // add some config options
            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Birthday notification",
                tooltip: () => "Shows a wake-up message when today is an NPC's birthday.",
                getValue: () => this.Config.BirthdayNotification,
                setValue: value => this.Config.BirthdayNotification = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Birthday quest",
                tooltip: () => "Adds a one-day birthday gift quest to your quest log.",
                getValue: () => this.Config.BirthdayQuest,
                setValue: value => this.Config.BirthdayQuest = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Loved gifts hint",
                tooltip: () => "Adds a list of loved gifts to the birthday quest text.",
                getValue: () => this.Config.LovedGiftsHint,
                setValue: value => this.Config.LovedGiftsHint = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Schedule hint",
                tooltip: () => "Adds the birthday NPC's schedule to the gifting quest",
                getValue: () => this.Config.NpcScheduleHint,
                setValue: value => this.Config.NpcScheduleHint = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Skip unknown NPCs",
                tooltip: () => "Skip notifications for NPCs you haven't met yet.",
                getValue: () => this.Config.SkipUnknownNpcs,
                setValue: value => this.Config.SkipUnknownNpcs = value
            );

        }

        /*********
        ** helper funcs - getting birthdays
        *********/

        // get birthdays - key: season/ day and value: dict: <npc name, npc display name>
        private Dictionary< (Season season, int Day), Dictionary<string, string>> GetAllBirthdays()
        {
            var birthdays = new Dictionary< (Season season, int Day), Dictionary<string, string>>();

            foreach (var npc in allCharacterData)
            {
                
                CharacterData data = npc.Value;

                // mod compatible way - BirthSeason could be null
                if (data.BirthSeason is null)
                {
                    continue;
                }

                // skip if not sociable 
                if (data.CanSocialize == "false")
                {
                    continue;
                }

                var birthSeasonDay = (data.BirthSeason.Value, data.BirthDay);

                // list to guard against mod NPCs have the same birthday as original NPCs
                if (!birthdays.ContainsKey(birthSeasonDay)){
                    birthdays[birthSeasonDay] = new Dictionary<string, string>();
                }

                birthdays[birthSeasonDay].Add(npc.Key, TokenParser.ParseText(data.DisplayName));
            }

            return birthdays;
        }

        /*********
        ** helper funcs - getting NPC gift taste
        *********/

        private static List<string> NormaliseTasteString(string tasteString)
        {
            return tasteString.Split(" ").ToList();
        }

        private List<string> GetLovedGiftNames(string npc)
        {

            var universalLove = NormaliseTasteString(allGiftTaste["Universal_Love"]);

            // mimic npc.getGiftTasteForThisItem manual override for Stardrop Tea
            universalLove.Add("StardropTea");

            if (!allGiftTaste.TryGetValue(npc, out var npcGiftTaste))
            {
                // guard - some mods might not add gift taste to their npcs
                return new List<string>();
            }

            var blocks = npcGiftTaste.Split("/");

            var loved = NormaliseTasteString(blocks[1]);
            var liked = NormaliseTasteString(blocks[3]);
            var disliked = NormaliseTasteString(blocks[5]);
            var hated = NormaliseTasteString(blocks[7]);
            var neutral = NormaliseTasteString(blocks[9]);

            // var npcLoved = universalLove.Concat(loved).ToList();

            var delete = liked.Concat(disliked).Concat(hated).Concat(neutral).ToList();

            universalLove.RemoveAll(item => delete.Contains(item));
            var npcLoved = universalLove.Concat(loved).ToList();

            var lovedItems = new List<string>();

            foreach (var id in npcLoved){
                if (allObjectData.TryGetValue(id, out var itemData))
                {
                    lovedItems.Add(TokenParser.ParseText(itemData.DisplayName));
                }
            }

            lovedItems.Sort();

            return lovedItems;
        }

        /*********
        ** load save - load all birthdays + all object items
        *********/

        private Dictionary< (Season season, int Day), Dictionary<string, string>> allBirthday = new();
        private Dictionary<string, CharacterData> allCharacterData = new();
        private Dictionary<string, string> allGiftTaste = new();
        private Dictionary<string, ObjectData> allObjectData = new();

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            allCharacterData = this.Helper.GameContent.Load<Dictionary<string, CharacterData>>("Data/Characters");
            allBirthday = this.GetAllBirthdays();
            allGiftTaste = this.Helper.GameContent.Load<Dictionary<string, string>>("Data/NPCGiftTastes");
            allObjectData = this.Helper.GameContent.Load<Dictionary<string, ObjectData>>("Data/Objects");
        }

        /*********
        ** day starts - load today's birthday npcs & add quest and notifications
        *********/

        private Dictionary<string, string> GetTodayBirthdayNpcs()
        {
            var currDate = SDate.Now();
            var today = (currDate.Season, currDate.Day);

            var birthdays = this.allBirthday; // elems of dict <string, string> 

            if (birthdays.TryGetValue(today, out var birthdayNpcs))
            {
                var todayBirthdays = new Dictionary<string, string>(birthdayNpcs);

                if (Game1.year < 2)
                {
                    todayBirthdays.Remove("Kent");
                }

                return new Dictionary<string, string>(todayBirthdays);
            }
            return new Dictionary<string, string>();

        }

        private bool IsNpcKnown(string npc) => Game1.player.friendshipData.ContainsKey(npc);

        private Dictionary<string, string> birthdayNpc =  new Dictionary<string, string>();

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            this.Helper.GameContent.InvalidateCache("Data/SpecialOrders");
            birthdayNpc =  this.GetTodayBirthdayNpcs();

            if (!this.Config.BirthdayQuest)
            {
                return;
            }

            foreach (var npc in birthdayNpc){
                if (this.Config.SkipUnknownNpcs && !IsNpcKnown(npc.Key))
                {
                    continue;
                }

                AddBirthdayQuest(npc.Key);
            }

            AttachGiftHooks();
            //ShowNextBirthdayNotification();
        }

        /*********
        ** Pronouns
        *********/

        private record PronounSet(string Subject, string Object, string Possessive, string LoveVerb);

        private PronounSet GetPronouns(string npc)
        {
            var pronounsUnknown = new PronounSet("they", "them", "their", "love");

            if (!this.allCharacterData.TryGetValue(npc, out var data))
            {
                return pronounsUnknown;
            }

            switch (data.Gender)
            {
                case Gender.Male:
                    return new PronounSet("he", "him", "his", "loves");

                case Gender.Female:
                    return new PronounSet("she", "her", "her", "loves");

                case Gender.Undefined:
                    return pronounsUnknown;

                default:
                    return pronounsUnknown;

            }
        }

        /*********
        ** Quests
        *********/

        private string FancyJoin(List<string> lovedItems, PronounSet pronoun)
        {
            var loveWord = pronoun.LoveVerb;
            var subject = char.ToUpper(pronoun.Subject[0]) + pronoun.Subject[1..];

            if (lovedItems.Count == 1)
            {
                return $"\n\n{subject} {loveWord} " + lovedItems[0] + ".";
            }

            var front = lovedItems.Take(lovedItems.Count - 1);
            var last = lovedItems[lovedItems.Count - 1];

            return $"\n\n{subject} {loveWord} " + string.Join(", ", front) + ", and " + last + ".";
        }

        // Register all birthday quests to special order data
        private SpecialOrderData BuildBirthdaySpecialOrderData(string npc, string npcDisplayName){
            
            var newSpecialOrder = new SpecialOrderData();
            newSpecialOrder.Name = $"{npcDisplayName}'s birthday";
            newSpecialOrder.Requester = npc;
            newSpecialOrder.Duration = QuestDuration.OneDay;
            // add custom OrderType to avoid quests showing up on town board + prize ticket reward
            newSpecialOrder.OrderType = "BirthdayQuest";

            var pronouns = this.GetPronouns(npc);

            var baseText =  $"It's {npcDisplayName}'s Birthday today! \nGive {pronouns.Object} something nice. ";

            if (this.Config.LovedGiftsHint)
            {
                var lovedItems = this.GetLovedGiftNames(npc);
                if (lovedItems.Count > 0)
                {
                    var lovedItemsText = this.FancyJoin(lovedItems, pronouns);
                    baseText = baseText + lovedItemsText;
                }
            }

            if (this.Config.NpcScheduleHint)
            {
                var scheduleString = this.GetTodayNpcSchedule(npc, pronouns);
                if (scheduleString != string.Empty)
                {
                    baseText += "\n\n" + scheduleString;
                }
            }

            //var likedItems = this.GetItemByTaste(npc, "like");
            //var likedItemsText = "\n\nThey like " + string.Join(", ", likedItems) + ".";

            newSpecialOrder.Text = baseText;

            // add objective to order; need SpecialOrderObjectiveData
            var newObjective = new SpecialOrderObjectiveData();
            newObjective.Type = "Gift";
            newObjective.Text = $"Give {npcDisplayName} a birthday gift.";
            newObjective.RequiredCount = "1";

            // use AcceptedContextTags to stop quest from auto gifting, so we can set up own hook
            newObjective.Data = new Dictionary<string, string>{{"AcceptedContextTags", "_bday_quest_placeholder"}, {"MinimumLikeLevel", "None"}};
            newSpecialOrder.Objectives = new List<SpecialOrderObjectiveData> {newObjective};

            // add rewards to order; need SpecialOrderRewardData
            var newRewards = new SpecialOrderRewardData();
            newRewards.Type = "Money";
            newRewards.Data = new Dictionary<string, string>{{"Amount", "1"}, {"Multiplier", "0"}};
            newSpecialOrder.Rewards = new List<SpecialOrderRewardData> {newRewards};

            return newSpecialOrder;
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            //here
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/SpecialOrders"))
            {
                return;
            }

            // not sure when is AssetRequested fired
            // gaurd against if AssetRequested is run before SaveLoaded
            if (this.allCharacterData.Count == 0)
            {
                allCharacterData = this.Helper.GameContent.Load<Dictionary<string, CharacterData>>("Data/Characters");
                allBirthday = this.GetAllBirthdays();
            }

            if (this.Config.LovedGiftsHint && this.allGiftTaste.Count == 0)
            {
                allGiftTaste = this.Helper.GameContent.Load<Dictionary<string, string>>("Data/NPCGiftTastes");
                allObjectData = this.Helper.GameContent.Load<Dictionary<string, ObjectData>>("Data/Objects");
            }

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, SpecialOrderData>().Data;

                foreach (var birthday in allBirthday)
                {
                    foreach (var npc in birthday.Value)
                    {
                        if (this.Config.SkipUnknownNpcs && !IsNpcKnown(npc.Key))
                        {
                            continue;
                        }

                        string orderId = $"BirthdayQuest.{npc.Key}.BirthdayGift";
                        data[orderId] = this.BuildBirthdaySpecialOrderData(npc.Key, npc.Value);
                    }
                }
            });
        }

        // add quest to active quests for birthday npcs
        private void AddBirthdayQuest(string npc)
        {
            var orderId = $"BirthdayQuest.{npc}.BirthdayGift";

            this.Monitor.Log($"added {orderId} to active!", LogLevel.Info);

            Game1.player.team.AddSpecialOrder(orderId, forceRepeatable: true);

        }

        // set up own gift hooks
        private void AttachGiftHooks()
        {
            foreach (SpecialOrder specialOrder in Game1.player.team.specialOrders)
            {

                var questName = specialOrder.questKey.Value;
                if (questName.StartsWith("BirthdayQuest."))
                {
                    specialOrder.onGiftGiven += (farmer, npc, item) => this.OnBirthdayGiftGiven(specialOrder, npc);
                }
                
            }
        }

        private void OnBirthdayGiftGiven(SpecialOrder specialOrder, NPC npc)
        {
            if (specialOrder.requester.Value != npc.Name)
            {
                return;
            }
            this.Monitor.Log($"gift hook: to {npc.Name}; order id {specialOrder.questKey.Value} ", LogLevel.Info);

            specialOrder.objectives[0].IncrementCount(1);
        }

        /*********
        ** Notifications
        *********/
        private void BirthdayNotification(string npc, string npcDisplayName)
        {
            var pronoun = this.GetPronouns(npc);

            string message = $"It's {npcDisplayName}'s Birthday today! ^Consider giving {pronoun.Object} something nice.";
            Game1.activeClickableMenu = new DialogueBox(message);
        }

        private void ShowNextBirthdayNotification()
        {
            if (birthdayNpc.Count == 0)
            {
                return;
            }

            if (!this.Config.BirthdayNotification)
            {
                return;
            }

            var npc = birthdayNpc.Keys.First();
            var npcDisplayName = birthdayNpc[npc];
            this.Monitor.Log($"{npc}'s birthday", LogLevel.Info);
            BirthdayNotification(npc, npcDisplayName);
            birthdayNpc.Remove(npc);
        }

        private void OnClosedMenu(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is not null)
            {
                return;
            }

            if (birthdayNpc.Count == 0)
            {
                return;
            }

            ShowNextBirthdayNotification();

        }

        // notification shows after black screen disappears - fix with check below
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            //here
            if (birthdayNpc.Count == 0)
            {
                return;
            }

            // don't show notification yet if still loading and black screen
            if (Game1.IsFading())
            {
                return;
            }

            // if there is a current pop up, wait
            if (Game1.activeClickableMenu is not null)
            {
                return;
            }

            ShowNextBirthdayNotification();

        }

        /*********
        ** Schedules
        *********/
        private string GetTodayNpcSchedule(string npcName, PronounSet pronouns)
        {
            var npc = Game1.getCharacterFromName(npcName);
            var output = string.Empty;

            if (npc is null)
            {
                return output; // skip if char not available
            }

            if (npc.Schedule is null)
            {
                npc.TryLoadSchedule(); // try reloading if no valid schedule
            }
            var schedule = npc.Schedule;
            if (schedule is null || schedule.Count == 0)
            {
                return output;
            }

            foreach (var move in schedule.Values.OrderBy(v => v.time))
            {
               var time = this.FormatTime(move.time);
               var dest = this.PrettifyLocationName(move.targetLocationName);
               output += $"\n{time} - {dest}";
            }

            var possessive = char.ToUpper(pronouns.Possessive[0]) + pronouns.Possessive[1..];
            output = possessive + " schedule today is:" + output;

            return output;
        }

        private string FormatTime(int time)
        {
            var hour = (time / 100) % 24;
            var mins = time % 100;

            bool isPm = hour >=12;

            string suffix = isPm ? "pm" : "am";

            if (isPm)
            {
                hour -= 12;
            }
            if (hour == 0)
            {
                hour = 12;
            }

            var strHour = hour.ToString();
            var strMins = mins.ToString();

            if (strMins.Length == 1){
                strMins = "0" + strMins;
            }

            return strHour + ":" + strMins + " " + suffix;
        }

        private string PrettifyLocationName(string locationId)
        {
            return Game1.getLocationFromName(locationId)?.DisplayName ?? locationId;
        }
    }
}
