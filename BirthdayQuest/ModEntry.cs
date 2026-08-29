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
        ** i18n helpers
        *********/

        /// <summary>Get a translation, falling back to the default (English) text.</summary>
        private string T(string key)
        {
            return this.Helper.Translation.Get(key);
        }

        /// <summary>Get a translation with tokens, falling back to the default (English) text.</summary>
        private string T(string key, object tokens)
        {
            return this.Helper.Translation.Get(key, tokens);
        }

        /// <summary>Uppercase the first character of a word (safe for empty strings).</summary>
        private static string Cap(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return char.ToUpper(text[0]) + text[1..];
        }

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
                name: () => this.T("config.birthday-notification.name"),
                tooltip: () => this.T("config.birthday-notification.tooltip"),
                getValue: () => this.Config.BirthdayNotification,
                setValue: value => this.Config.BirthdayNotification = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.T("config.birthday-quest.name"),
                tooltip: () => this.T("config.birthday-quest.tooltip"),
                getValue: () => this.Config.BirthdayQuest,
                setValue: value => this.Config.BirthdayQuest = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.T("config.loved-gifts-hint.name"),
                tooltip: () => this.T("config.loved-gifts-hint.tooltip"),
                getValue: () => this.Config.LovedGiftsHint,
                setValue: value => this.Config.LovedGiftsHint = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.T("config.schedule-hint.name"),
                tooltip: () => this.T("config.schedule-hint.tooltip"),
                getValue: () => this.Config.NpcScheduleHint,
                setValue: value => this.Config.NpcScheduleHint = value
            );

            configMenu.AddBoolOption(
                mod: this.ModManifest,
                name: () => this.T("config.skip-unknown-npcs.name"),
                tooltip: () => this.T("config.skip-unknown-npcs.tooltip"),
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

            // sort by the player's language rules (accented names land in the right place in PT-BR)
            lovedItems.Sort(StringComparer.CurrentCulture);

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

        /// <summary>Build a pronoun set from the i18n files, so each translation picks its own wording.</summary>
        private PronounSet GetPronounSet(string genderKey)
        {
            return new PronounSet(
                this.T($"pronouns.{genderKey}.subject"),
                this.T($"pronouns.{genderKey}.object"),
                this.T($"pronouns.{genderKey}.possessive"),
                this.T($"pronouns.{genderKey}.love-verb")
            );
        }

        private PronounSet GetPronouns(string npc)
        {
            if (!this.allCharacterData.TryGetValue(npc, out var data))
            {
                return this.GetPronounSet("unknown");
            }

            switch (data.Gender)
            {
                case Gender.Male:
                    return this.GetPronounSet("male");

                case Gender.Female:
                    return this.GetPronounSet("female");

                case Gender.Undefined:
                    return this.GetPronounSet("unknown");

                default:
                    return this.GetPronounSet("unknown");

            }
        }

        /// <summary>Build the token bag handed to every translated string.</summary>
        private object BuildTokens(PronounSet pronouns, string? npcDisplayName = null, string? items = null)
        {
            return new
            {
                npc = npcDisplayName ?? string.Empty,
                items = items ?? string.Empty,
                subject = pronouns.Subject,
                subjectCap = Cap(pronouns.Subject),
                objectPronoun = pronouns.Object,
                objectPronounCap = Cap(pronouns.Object),
                possessive = pronouns.Possessive,
                possessiveCap = Cap(pronouns.Possessive),
                loveVerb = pronouns.LoveVerb
            };
        }

        /*********
        ** Quests
        *********/

        private string FancyJoin(List<string> lovedItems, PronounSet pronoun)
        {
            string items;

            if (lovedItems.Count == 1)
            {
                items = lovedItems[0];
            }
            else
            {
                var front = lovedItems.Take(lovedItems.Count - 1);
                var last = lovedItems[^1];
                items = string.Join(", ", front) + this.T("list.final-separator") + last;
            }

            return "\n\n" + this.T("quest.loved-gifts", this.BuildTokens(pronoun, items: items));
        }

        // Register all birthday quests to special order data
        private SpecialOrderData BuildBirthdaySpecialOrderData(string npc, string npcDisplayName){

            var newSpecialOrder = new SpecialOrderData();
            var pronouns = this.GetPronouns(npc);
            var tokens = this.BuildTokens(pronouns, npcDisplayName);

            newSpecialOrder.Name = this.T("quest.name", tokens);
            newSpecialOrder.Requester = npc;
            newSpecialOrder.Duration = QuestDuration.OneDay;
            // add custom OrderType to avoid quests showing up on town board + prize ticket reward
            newSpecialOrder.OrderType = "BirthdayQuest";

            var baseText = this.T("quest.text", tokens);

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

            newSpecialOrder.Text = baseText;

            // add objective to order; need SpecialOrderObjectiveData
            var newObjective = new SpecialOrderObjectiveData();
            newObjective.Type = "Gift";
            newObjective.Text = this.T("quest.objective", tokens);
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
            var pronouns = this.GetPronouns(npc);

            string message = this.T("notification.message", this.BuildTokens(pronouns, npcDisplayName));

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

            output = this.T("schedule.header", this.BuildTokens(pronouns)) + output;

            return output;
        }

        /// <summary>Format a schedule time using the game's own clock format (follows the game language and 24-hour setting).</summary>
        private string FormatTime(int time)
        {
            try
            {
                var formatted = Game1.getTimeOfDayString(time);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    return formatted.Trim();
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"could not format time {time} with the game formatter, using the 12-hour fallback: {ex.Message}", LogLevel.Trace);
            }

            // fallback - upstream 12-hour format
            var hour = (time / 100) % 24;
            var mins = time % 100;

            bool isPm = hour >= 12;

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
