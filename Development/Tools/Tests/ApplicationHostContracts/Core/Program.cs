using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.Hosting;
using BEngine.Platform.DotNet;
using BEngine.ProjectSystem;
using Debug=BEngine.Debug;

var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("BENGINE_HUB_DIRECTORY",output);
DotNetPlatformServices.Initialize();
var initialDirectories=Directory.GetDirectories(output).Order().ToArray();
var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject<T>(Action action,string name) where T:Exception{try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
var company=Application.companyName;var product=Application.productName;var background=Application.runInBackground;
var tags=TagManager.version;var layers=LayerRegistry.version;var log=Debug.logger.logHandler;var captured=new CapturedLog();
var focused=Application.isFocused;var focus=new List<bool>();var pauses=new List<bool>();var low=0;
void Focus(bool value)=>focus.Add(value);void Pause(bool value)=>pauses.Add(value);void Low()=>low++;
Application.focusChanged+=Focus;Application.pauseStateChanged+=Pause;Application.lowMemory+=Low;
try{
 RuntimeConfiguration.ApplyIdentity("Host Company","Host Product");
 Check(Application.companyName=="Host Company" && Application.productName=="Host Product","Host identity applies both values through the engine configuration mechanism");
 Check(Application.runInBackground==background && TagManager.version==tags && LayerRegistry.version==layers,"Applying identity does not apply unrelated runtime settings");
 Reject<ArgumentNullException>(()=>RuntimeConfiguration.ApplyIdentity("New Company",null!),"Null product is rejected before identity mutation");
 Check(Application.companyName=="Host Company" && Application.productName=="Host Product","Rejected product does not partially change identity");
 Reject<ArgumentNullException>(()=>RuntimeConfiguration.ApplyIdentity(null!,"New Product"),"Null company is rejected before identity mutation");
 Check(Application.productName=="Host Product","Rejected company leaves the product unchanged");
 RuntimeConfiguration.ApplyIdentity(" ","");
 Check(Application.companyName==" " && Application.productName=="","Authoring draft identity keeps existing values without trimming or runtime activation");
 Reject<InvalidDataException>(()=>RuntimeConfiguration.Validate(new RuntimeSettingsData{CompanyName=" ",ProductName=""}),"Runtime activation still validates draft identity");
 ApplicationHostEvents.NotifyFocusChanged(!focused);ApplicationHostEvents.NotifyFocusChanged(!focused);
 Check(focus.SequenceEqual(new[]{!focused}) && Application.isFocused==!focused,"Host focus forwarding preserves duplicate suppression and focused state");
 ApplicationHostEvents.NotifyFocusChanged(focused);
 Check(focus.SequenceEqual(new[]{!focused,focused}),"Returning focus emits the existing application notification");
 ApplicationHostEvents.NotifyPauseChanged(true);ApplicationHostEvents.NotifyPauseChanged(true);ApplicationHostEvents.NotifyPauseChanged(false);
 Check(pauses.SequenceEqual(new[]{true,true,false}),"Host pause notifications retain their existing delivery semantics");
 ApplicationHostEvents.NotifyLowMemory();ApplicationHostEvents.NotifyLowMemory();
 Check(low==2,"Low-memory notifications reach the existing application event");
 Debug.logger.logHandler=captured;void Fail(bool value)=>throw new InvalidOperationException("Host callback fixture");
 Application.pauseStateChanged+=Fail;
 try{ApplicationHostEvents.NotifyPauseChanged(true);Check(captured.Errors==1,"Host callback failure retains engine error containment");}finally{Application.pauseStateChanged-=Fail;}
 Check(Directory.GetDirectories(output).Order().SequenceEqual(initialDirectories),"Lifecycle forwarding and identity configuration do not create storage folders");
 Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "BEngine.Core.Editor" or "BEngine.Graphics"),"Lifecycle notifications require no editor or drawing implementation");
 Check(!typeof(BObject).Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Any(a=>a.AssemblyName==Assembly.GetExecutingAssembly().GetName().Name),"The host consumer has no private Core grant");
}finally{Application.focusChanged-=Focus;Application.pauseStateChanged-=Pause;Application.lowMemory-=Low;ApplicationHostEvents.NotifyFocusChanged(focused);RuntimeConfiguration.ApplyIdentity(company,product);Debug.logger.logHandler=log;}
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks.Count,Details=checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("APPLICATION_HOST_CORE_OK|"+checks.Count);
public sealed class CapturedLog:ILogHandler
{public int Errors;public void LogFormat(LogType type,BObject? context,string format,params object?[] args){if(type==LogType.Error)Errors++;}public void LogException(Exception error,BObject? context)=>Errors++;}
