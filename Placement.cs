using System;
using System.Collections.Generic;
using System.Windows;

// Physical screen coordinates only. No host text or document content is retained.
public sealed class LayoutSnapshot {
    public Rect composer=Rect.Empty;
    public Rect viewport=Rect.Empty;
    public readonly List<Rect> obstacles=new List<Rect>();
}
public sealed class Placement {
    public Rect bounds=Rect.Empty;
    public string mode="hidden";
    public string reason="no-composer";
    public bool Visible { get { return !bounds.IsEmpty; } }
}
public static class PlacementEngine {
    static bool Clear(Rect candidate,LayoutSnapshot layout,double scale) {
        if(candidate.IsEmpty || !layout.viewport.Contains(candidate)) return false;
        var padded=candidate; padded.Inflate(2*scale,2*scale);
        if(padded.IntersectsWith(layout.composer)) return false;
        foreach(var obstacle in layout.obstacles) if(padded.IntersectsWith(obstacle)) return false;
        return true;
    }
    public static Placement Choose(LayoutSnapshot layout,Rect window,double scale,OverlayConfig config) {
        var result=new Placement();
        if(layout.composer.IsEmpty || layout.viewport.IsEmpty) return result;
        double dipWidth=Math.Max(180,Math.Min(config.widthDip,layout.composer.Width/scale-24));
        double width=Math.Round(dipWidth*scale),height=Math.Ceiling((dipWidth*28/216+29)*scale);
        var full=new Rect(layout.composer.Left+layout.composer.Width/2-width/2+config.offsetXDip*scale,
            layout.composer.Top-8*scale-height+config.offsetYDip*scale,width,height);
        if(config.manualAnchor) full=new Rect(window.Left+window.Width*config.centerX-width/2,
            window.Bottom-config.bottomDip*scale-height,width,height);
        if(Clear(full,layout,scale)) { result.bounds=full; result.mode="scene"; result.reason="space-above-composer"; return result; }
        // The small robot lives in an empty side gutter, with its label included
        // in collision checks. Never place a guessed strip on a non-chat page.
        width=Math.Round(56*scale); height=Math.Ceiling(80*scale);
        double top=Math.Max(layout.viewport.Top+8*scale,Math.Min(layout.composer.Top+8*scale+config.offsetYDip*scale,
            layout.composer.Bottom-height-4*scale));
        var right=new Rect(layout.composer.Right+8*scale+config.offsetXDip*scale,top,width,height);
        var left=new Rect(layout.composer.Left-8*scale-width+config.offsetXDip*scale,top,width,height);
        if(Clear(right,layout,scale)) { result.bounds=right; result.mode="portrait"; result.reason="right-gutter"; }
        else if(Clear(left,layout,scale)) { result.bounds=left; result.mode="portrait"; result.reason="left-gutter"; }
        else result.reason="no-clear-space";
        return result;
    }
}
public static class PlacementTests {
    static void Expect(bool value,string message) { if(!value) throw new Exception("Placement: "+message); }
    public static void Run() {
        var config=new OverlayConfig {widthDip=380,centerX=.5,bottomDip=100};
        var window=new Rect(0,0,1600,1000);
        var layout=new LayoutSnapshot {composer=new Rect(500,600,640,140),viewport=new Rect(300,100,1300,900)};
        var clear=PlacementEngine.Choose(layout,window,1,config);
        Expect(clear.mode=="scene" && clear.bounds.Bottom<layout.composer.Top,"clear scene");
        layout.obstacles.Add(new Rect(700,540,200,40));
        var home=PlacementEngine.Choose(layout,window,1,config);
        Expect(home.mode=="portrait" && home.bounds.Left>layout.composer.Right,"new-chat heading avoided");
        layout.obstacles.Add(home.bounds);
        var left=PlacementEngine.Choose(layout,window,1,config);
        Expect(left.mode=="portrait" && left.bounds.Right<layout.composer.Left,"right control avoided");
        layout.obstacles.Add(left.bounds);
        Expect(!PlacementEngine.Choose(layout,window,1,config).Visible,"hide without clear space");
        Expect(!PlacementEngine.Choose(new LayoutSnapshot(),window,1,config).Visible,"no composer means hidden");
        var small=new LayoutSnapshot {composer=new Rect(10,130,300,100),viewport=new Rect(0,100,320,200)};
        Expect(!PlacementEngine.Choose(small,new Rect(0,0,320,300),1,config).Visible,"narrow page stays clear");
        var scaled=new LayoutSnapshot {composer=new Rect(1000,1200,1280,280),viewport=new Rect(600,200,2600,1800)};
        scaled.obstacles.Add(new Rect(1400,1080,400,80));
        var highDpi=PlacementEngine.Choose(scaled,new Rect(0,0,3200,2000),2,config);
        Expect(highDpi.mode=="portrait" && highDpi.bounds.Width==112 && highDpi.bounds.Height==160,"200 percent DPI");
        scaled.composer=new Rect(1000,1800,1280,72);
        scaled.obstacles.Add(new Rect(1000,1650,1280,140));
        var shortInput=PlacementEngine.Choose(scaled,new Rect(0,0,3200,2000),2,config);
        Expect(shortInput.mode=="portrait" && shortInput.bounds.Bottom<scaled.composer.Bottom,"short composer gutter");
        config.offsetXDip=-1000; config.offsetYDip=1000;
        Expect(!PlacementEngine.Choose(layout,window,1,config).Visible,"unsafe nudge hides");
        config.offsetXDip=0; config.offsetYDip=0;
        config.manualAnchor=true; config.centerX=.5; config.bottomDip=400;
        var manual=PlacementEngine.Choose(layout,window,1,config);
        Expect(!manual.Visible,"manual position still respects text");
    }
}
