using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime;
using RogueKingdom.Game.UI;
using RogueKingdom.Core.Domain;
using RogueKingdom.Game;
using HarmonyLib;

namespace Autolantis.SkipFix;
[BepInPlugin("local.autolantis.skipfix","Autolantis Skip Fix","0.2.0")]
public sealed class Plugin:BasePlugin
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MoveNext(IntPtr self,IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventIndex(IntPtr self,IntPtr ev,IntPtr method);
    private MoveNext _original=null!,_hook=null!,_prepare=null!;
    private EventIndex _index=null!;
    private INativeDetour? _detour;
    private IntPtr _prepareClass;
    private long _collapsed,_lastLog;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IconAnimation(IntPtr self,IntPtr rt,IntPtr settings,int owner,IntPtr method);
    private IconAnimation _hitOriginal=null!,_scaleOriginal=null!,_hitHook=null!,_scaleHook=null!;
    private INativeDetour? _hitDetour,_scaleDetour;
    private readonly HashSet<(IntPtr view,IntPtr icon,int owner,int kind)> _seen=new();
    private int _animationFrame=-1;
    private long _allowed,_suppressed,_lastFrame,_diagStart;
    private double _maxGap,_maxCompute;
    private int _frames;
    private static Plugin _instance=null!;
    private readonly Harmony _harmony=new("local.autolantis.skipfix.diagnostics");
    public override void Load()
    {
        _instance=this;
        if(!Config.Bind("Optimization","Enabled",true,"Collapse only empty PrepareSeasonWave waits inside an already completed season. Restart required.").Value)return;
        using var stream=File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll"));
        using var sha=SHA256.Create();
        if(Convert.ToHexString(sha.ComputeHash(stream))!="E8D4E7E8C484562BFA1B00A59B17AD582CE384CE96255B24AC8C6F90DA1DE4F4")
        {Log.LogError("Unsupported build; no hooks installed.");return;}
        var nested=typeof(SettlementPlayer).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic);
        var replay=nested.Single(t=>t.Name=="_Replay_d__122");
        var prepare=nested.Single(t=>t.Name=="_PrepareSeasonWave_d__186");
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(prepare.TypeHandle);
        _prepareClass=(IntPtr)typeof(Il2CppClassPointerStore<>).MakeGenericType(prepare).GetField("NativeClassPtr")!.GetValue(null)!;
        _prepare=Marshal.GetDelegateForFunctionPointer<MoveNext>(Native(prepare,"MoveNext"));
        _index=Marshal.GetDelegateForFunctionPointer<EventIndex>(Native(typeof(SettlementPlayer),"EventIndex"));
        if(Environment.GetCommandLineArgs().Contains("--skipfix-selftest"))SelfTest();
        _hook=Advance;
        _detour=INativeDetour.CreateAndApply(Native(replay,"MoveNext"),_hook,out _original);
        if(Config.Bind("Optimization","CoalesceArrivalAnimations",true,"At most one hit and scale animation per icon, replay owner and frame. Resource deliveries are retained.").Value)
        {
            _hitHook=(self,rt,settings,owner,method)=>{if(AllowAnimation(UnityEngine.Time.frameCount,self,rt,owner,0))_hitOriginal(self,rt,settings,owner,method);};
            _scaleHook=(self,rt,settings,owner,method)=>{if(AllowAnimation(UnityEngine.Time.frameCount,self,rt,owner,1))_scaleOriginal(self,rt,settings,owner,method);};
            _hitDetour=INativeDetour.CreateAndApply(Native(typeof(ResourceListView),"PlayHitTween"),_hitHook,out _hitOriginal);
            _scaleDetour=INativeDetour.CreateAndApply(Native(typeof(ResourceListView),"PlayScalePunch"),_scaleHook,out _scaleOriginal);
            Log.LogInfo("SkipFix: repeated resource icon animations coalesced within each frame; deliveries unchanged.");
        }
        if(Config.Bind("Diagnostics","Enabled",true,"Lightweight frame summaries and compute timing; no separate diagnostic plugin required.").Value)
        {
            _harmony.Patch(AccessTools.Method(typeof(GameRoot),"Update"),prefix:new HarmonyMethod(typeof(Plugin),nameof(Frame)));
            _harmony.Patch(AccessTools.Method(typeof(GameSession),"AdvanceSettlement"),prefix:new HarmonyMethod(typeof(Plugin),nameof(ComputeStart)),postfix:new HarmonyMethod(typeof(Plugin),nameof(ComputeEnd)));
        }
        _lastLog=Stopwatch.GetTimestamp();
        Log.LogInfo("SkipFix active: completed-season empty waits only; max 256 collapses / 2 ms per call.");
    }
    private bool AllowAnimation(int frame,IntPtr self,IntPtr rt,int owner,int kind)
    {
        // Owner 0/negative is outside tracked settlement playback; keep original behavior.
        if(owner<=0||rt==IntPtr.Zero){_allowed++;return true;}
        if(_animationFrame!=frame){_seen.Clear();_animationFrame=frame;}
        if(_seen.Add((self,rt,owner,kind))){_allowed++;return true;}
        _suppressed++;return false;
    }
    private static void ComputeStart(out long __state)=>__state=Stopwatch.GetTimestamp();
    private static void ComputeEnd(long __state)=>_instance._maxCompute=Math.Max(_instance._maxCompute,(Stopwatch.GetTimestamp()-__state)*1000.0/Stopwatch.Frequency);
    private static void Frame()
    {
        var p=_instance;var now=Stopwatch.GetTimestamp();
        if(p._lastFrame!=0)
        {
            var gap=(now-p._lastFrame)*1000.0/Stopwatch.Frequency;p._maxGap=Math.Max(p._maxGap,gap);p._frames++;
            if(gap>=100)p.Log.LogInfo($"SKIP-FIX STALL gapMs={gap:F1} frame={UnityEngine.Time.frameCount} focused={UnityEngine.Application.isFocused} maxComputeMs={p._maxCompute:F1} animationsSuppressed={p._suppressed}");
        }
        p._lastFrame=now;
        if(p._diagStart==0){p._diagStart=now;return;}
        var elapsed=(now-p._diagStart)*1000.0/Stopwatch.Frequency;
        if(elapsed<5000)return;
        p.Log.LogInfo($"SKIP-FIX DIAG windowMs={elapsed:F1} frames={p._frames} maxGapMs={p._maxGap:F1} maxComputeMs={p._maxCompute:F1} allowedAnimations={p._allowed} suppressedAnimations={p._suppressed} collapsedEmptyWaits={p._collapsed}");
        p._diagStart=now;p._frames=0;p._maxGap=0;p._maxCompute=0;
    }
    private static IntPtr Native(Type type,string name)
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        var f=type.GetFields(BindingFlags.Static|BindingFlags.NonPublic).Single(x=>x.Name.StartsWith("NativeMethodInfoPtr_"+name+"_"));
        return Marshal.ReadIntPtr((IntPtr)f.GetValue(null)!);
    }
    private bool EmptyPrepare(IntPtr replay)
    {
        // Offsets verified against this exact build's IL2CPP metadata.
        if(Marshal.ReadInt32(replay,0x10)!=3)return false;
        var child=Marshal.ReadIntPtr(replay,0x18);
        var owner=Marshal.ReadIntPtr(replay,0x20);
        if(child==IntPtr.Zero||owner==IntPtr.Zero||Marshal.ReadIntPtr(child)!=_prepareClass||
           Marshal.ReadInt32(child,0x10)!=0||Marshal.ReadIntPtr(child,0x20)!=owner)return false;
        // Complete=3: summary and structure application have already finished.
        if(Marshal.ReadInt32(owner,0x210)!=3||Marshal.ReadIntPtr(owner,0x228)==IntPtr.Zero||
           Marshal.ReadIntPtr(owner,0x240)==IntPtr.Zero)return false;
        var season=Marshal.ReadIntPtr(owner,0x230);
        var ev=Marshal.ReadIntPtr(child,0x28);
        if(season==IntPtr.Zero||ev==IntPtr.Zero)return false;
        var index=_index(owner,ev,IntPtr.Zero);
        // At the boundary PrepareSeasonWave must run normally to finish/begin seasons.
        return index>=Marshal.ReadInt32(season,0x14)&&index<Marshal.ReadInt32(season,0x18);
    }
    private byte Advance(IntPtr self,IntPtr method)
    {
        var start=Stopwatch.GetTimestamp();
        byte result=_original(self,method);
        int count=0;
        while(result!=0&&count<256&&(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency<2&&EmptyPrepare(self))
        {
            // The verified child would return false immediately without side effects.
            // Resume the original parent; it retains event ordering and every season boundary.
            result=_original(self,method);
            count++;
        }
        if(count>0 && _collapsed==0)Log.LogInfo("SKIP-FIX first empty waits collapsed; count="+count);
        _collapsed+=count;
        if(result==0 && _collapsed>0)Log.LogInfo("SKIP-FIX replay completed; cumulativeCollapsedEmptyWaits="+_collapsed);
        if(count>0&&(Stopwatch.GetTimestamp()-_lastLog)>5*Stopwatch.Frequency)
        {
            Log.LogInfo("SKIP-FIX collapsedEmptyWaits="+_collapsed);
            _lastLog=Stopwatch.GetTimestamp();
        }
        return result;
    }
    private void SelfTest()
    {
        // Unmanaged fixture buffers are never attached to the live scene or saved.
        var blocks=new List<IntPtr>();
        IntPtr Block(int size){var p=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,p,size);blocks.Add(p);return p;}
        var ev=new YearStarted(1);
        var indexes=new Il2CppSystem.Collections.Generic.Dictionary<DomainEvent,int>();
        indexes.Add(ev,5);
        try
        {
            var r=Block(0x100);var c=Block(0x40);var p=Block(0x280);var s=Block(0x40);
            Marshal.WriteInt32(r,0x10,3);Marshal.WriteIntPtr(r,0x18,c);Marshal.WriteIntPtr(r,0x20,p);
            Marshal.WriteIntPtr(c,_prepareClass);Marshal.WriteIntPtr(c,0x20,p);Marshal.WriteIntPtr(c,0x28,ev.Pointer);
            Marshal.WriteInt32(p,0x210,3);Marshal.WriteIntPtr(p,0x228,s);Marshal.WriteIntPtr(p,0x230,s);Marshal.WriteIntPtr(p,0x240,indexes.Pointer);
            Marshal.WriteByte(p,0x270,1);
            Marshal.WriteInt32(s,0x14,0);Marshal.WriteInt32(s,0x18,10);
            void Check(bool ok,string message){if(!ok)throw new Exception("SELFTEST: "+message);}
            Check(EmptyPrepare(r),"positive match");
            // Execute the actual game's native child on the fixture: no yield is expected.
            for(int i=0;i<1000;i++){Marshal.WriteInt32(c,0x10,0);Check(_prepare(c,IntPtr.Zero)==0,"native child yielded");}
            Marshal.WriteInt32(c,0x10,0);
            foreach(var phase in new[]{0,1,2}){Marshal.WriteInt32(p,0x210,phase);Check(!EmptyPrepare(r),"must retain unfinished season");}
            Marshal.WriteInt32(p,0x210,3);
            indexes[ev]=10;Check(!EmptyPrepare(r),"must retain season boundary");
            indexes[ev]=-1;Check(!EmptyPrepare(r),"must retain invalid index");
            indexes[ev]=5;Marshal.WriteInt32(r,0x10,9);Check(!EmptyPrepare(r),"must retain pause");
            Marshal.WriteInt32(r,0x10,3);Marshal.WriteIntPtr(p,0x230,IntPtr.Zero);Check(!EmptyPrepare(r),"must retain absent season");
            Log.LogInfo("SKIP-FIX SELFTEST PASS: native empty child x1000; unfinished phases, boundary, invalid index, pause and absent season preserved.");
            Check(AllowAnimation(100,p,s,1,0),"first hit retained");
            for(int i=0;i<5000;i++)Check(!AllowAnimation(100,p,s,1,0),"duplicate hit suppressed");
            Check(AllowAnimation(100,p,s,1,1),"scale independent of hit");
            Check(AllowAnimation(100,p,s,2,0),"new playback owner retained");
            Check(AllowAnimation(100,c,s,1,0),"other resource view retained");
            Check(AllowAnimation(100,p,c,1,0),"other icon retained");
            Check(AllowAnimation(101,p,s,1,0),"next frame retained");
            for(int i=0;i<2;i++)Check(AllowAnimation(101,p,s,0,0),"untracked animation retained");
            Log.LogInfo("SKIP-FIX ANIMATION SELFTEST PASS: 5000 duplicates coalesced; frame/icon/view/owner/type separation and untracked animations retained.");
            _seen.Clear();_animationFrame=-1;_allowed=0;_suppressed=0;
        }
        finally{foreach(var p in blocks)Marshal.FreeHGlobal(p);GC.KeepAlive(ev);GC.KeepAlive(indexes);}
    }
    public override bool Unload()=>false;
}
