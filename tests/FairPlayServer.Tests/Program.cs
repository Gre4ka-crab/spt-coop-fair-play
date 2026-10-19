using PrivateStack.FairPlay.Server;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.RaidSettings;
using System.Text.Json;
using FikaServer.Models.Fika.Routes.Client.Check;
int passed=0;
void Check(bool value,string test){if(!value)throw new Exception(test);passed++;Console.WriteLine("PASS "+test);}
for(int bits=0;bits<16;bits++){
 bool configured=(bits&1)!=0,online=(bits&2)!=0,same=(bits&4)!=0,spectator=(bits&8)!=0;bool allowed;
 try{RaidRules.Host(configured,online,same,spectator);allowed=true;}catch(InvalidOperationException){allowed=false;}
 Check(allowed==(configured&&online&&same&&!spectator),"headless identity/connection matrix "+bits);
}
var request=new GetRaidConfigurationRequestData{Location="factory4_day",TimeVariant=DateTimeEnum.PAST,MetabolismDisabled=true,PlayersSpawnPlace=PlayersSpawnPlace.AtTheEndsOfTheMap,BotSettings=new(){IsScavWars=true,BotAmount=BotAmount.NoBots},WavesSettings=new(){BotAmount=BotAmount.Horde,BotDifficulty=BotDifficulty.Impossible,IsBosses=false,IsTaggedAndCursed=true}};
RaidRules.Canonical(request);
Check(request.Location=="factory4_day"&&request.TimeVariant==DateTimeEnum.PAST,"map/day choice retained");
Check(request.TimeAndWeatherSettings.HourOfDay==-1,"native selected time is not overridden by fixed noon");
Check(request.MetabolismDisabled==false&&request.PlayersSpawnPlace==PlayersSpawnPlace.DifferentPlaces,"metabolism/spawn override removed");
Check(request.BotSettings.IsScavWars==false&&request.BotSettings.BotAmount==BotAmount.AsOnline,"scav wars/no bots removed");
Check(request.WavesSettings.BotAmount==BotAmount.AsOnline&&request.WavesSettings.BotDifficulty==BotDifficulty.AsOnline&&request.WavesSettings.IsBosses==true&&request.WavesSettings.IsTaggedAndCursed==false,"difficulty/boss/tagged overrides removed");
Check(RaidRules.Custom() is{UseCustomWeather:false,DisableOverload:false,DisableLegStamina:false,DisableArmStamina:false},"custom weather/overload/stamina disabled");
var left=RaidRules.Waves();left.IsBosses=false;Check(RaidRules.Waves().IsBosses==true,"fresh settings per request");
request.ExtensionData=new(){["unknownDifficultyOverride"]=true};bool denied=false;try{RaidRules.Canonical(request);}catch(InvalidOperationException){denied=true;}Check(denied,"unknown extension refused");
const string guid="private.spt.clientfairplay";
FikaCheckModResponse Empty()=>new(){Forbidden=new(),MissingRequired=new(),HashMismatch=new()};
var validation=Empty();ModRules.Validate(false,new(),validation,123);Check(validation.MissingRequired.SequenceEqual(new[]{guid}),"human addon missing uses native denial");
validation=Empty();ModRules.Validate(false,new(){[guid]=124},validation,123);Check(validation.HashMismatch.SequenceEqual(new[]{guid}),"human wrong addon hash denied");
validation=Empty();validation.Forbidden.AddRange(new[]{guid,"foreign.cheat"});validation.HashMismatch.Add("foreign.mod");validation.MissingRequired.Add("native.required");
ModRules.Validate(false,new(){[guid]=123},validation,123);Check(validation.Forbidden.SequenceEqual(new[]{"foreign.cheat"})&&validation.HashMismatch.SequenceEqual(new[]{"foreign.mod"})&&validation.MissingRequired.SequenceEqual(new[]{"native.required"}),"own exact build preserves every other native failure");
validation=Empty();ModRules.Validate(true,new(),validation,123);Check(validation.MissingRequired.Count==0,"headless does not require human addon");
validation=Empty();ModRules.Validate(true,new(){[guid]=123},validation,123);Check(validation.Forbidden.Contains(guid),"human addon forbidden on headless role");
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS_NATIVE_DTO_POLICY_NOT_LIVE_BACKEND",passed}));
