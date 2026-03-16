// MuseSense - Interactive Museum Guide
// Multi-screen: Explore | Favourites | Artifact Detail
// TUIO marker detection auto-navigates to Artifact page.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using TUIO;

// Minimal JSON parser for users.json (no external deps)
static class TinyJson
{
    public static List<UserRecord> ParseUsers(string json)
    {
        var result = new List<UserRecord>();
        int pos = 0;
        while (true)
        {
            int ni = json.IndexOf("\"name\"", pos);
            if (ni < 0) break;
            int colon = json.IndexOf(':', ni);
            int q1 = json.IndexOf('"', colon + 1);
            int q2 = json.IndexOf('"', q1 + 1);
            string name = json.Substring(q1 + 1, q2 - q1 - 1);
            var favs = new List<int>();
            int fi = json.IndexOf("\"favourites\"", q2);
            int nextName = json.IndexOf("\"name\"", q2 + 1);
            if (fi >= 0 && (nextName < 0 || fi < nextName))
            {
                int arrStart = json.IndexOf('[', fi);
                int arrEnd   = json.IndexOf(']', arrStart);
                string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
                if (!string.IsNullOrWhiteSpace(inner))
                    foreach (string tok in inner.Split(','))
                    { int v; if (int.TryParse(tok.Trim(), out v)) favs.Add(v); }
            }
            result.Add(new UserRecord { Name = name, Favourites = favs });
            pos = q2 + 1;
        }
        return result;
    }
}

class UserRecord
{
    public string Name;
    public List<int> Favourites = new List<int>();
}

// Artifact data
static class ArtifactData
{
    public static readonly string[] Names = {
        "Mask of Tutankhamun",
        "Ramses II Statue",
        "King Senwosret III"
    };
    public static readonly string[] Descriptions = {
        "The golden death mask of Pharaoh Tutankhamun, crafted around 1323 BC. " +
        "Made of solid gold inlaid with lapis lazuli, quartz, and obsidian.",
        "Colossal granite statue of Ramses II at the Grand Egyptian Museum, " +
        "depicting the most celebrated pharaoh of the New Kingdom (1279-1213 BC).",
        "Quartzite head of Senwosret III (1836-1818 BC), renowned for its " +
        "strikingly realistic, careworn expression."
    };
    public static readonly Color[] Colors = {
        Color.FromArgb(220, 191, 138, 42),
        Color.FromArgb(220, 160, 160, 160),
        Color.FromArgb(220, 205, 170, 125)
    };
    public static string GetObjPath(int id)
    {
        switch (id)
        {
            case 0: return Path.Combine("3d models","Mask of Tutankhamun","Mask of Tutankhamun.obj");
            case 1: return Path.Combine("3d models","Ramses II statue at the Grand Egyptian Museum","Ramses II statue at the Grand Egyptian Museum .obj");
            case 2: return Path.Combine("3d models","King Senwosret III (1836-1818 BC)","King Senwosret III (1836-1818 BC).obj");
            default: return null;
        }
    }
    public static int Count { get { return Names.Length; } }
}

enum AppScreen { Explore, Favourites, Artifact }

// 3-D geometry helpers
class Vec3 { public float X,Y,Z; public Vec3(float x,float y,float z){X=x;Y=y;Z=z;} }
class Vec2 { public float U,V; public Vec2(float u,float v){U=u;V=v;} }
class Face3 {
    public int A,B,C,TA,TB,TC; public string Mat;
    public Face3(int a,int b,int c,int ta,int tb,int tc,string m){A=a;B=b;C=c;TA=ta;TB=tb;TC=tc;Mat=m;}
}
class MaterialInfo { public Color Diffuse=Color.LightGray; public string TexPath; public Bitmap TexBmp; }
class ObjModel {
    public List<Vec3> Verts=new List<Vec3>();
    public List<Vec2> UVs=new List<Vec2>();
    public List<Face3> Faces=new List<Face3>();
    public Dictionary<string,Color> MatColors=new Dictionary<string,Color>();
    public Dictionary<string,Bitmap> MatTextures=new Dictionary<string,Bitmap>();
    public float Radius=1f;
}

public class TuioDemo : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long,TuioObject> objectList = new Dictionary<long,TuioObject>(128);
    private Dictionary<long,TuioCursor> cursorList = new Dictionary<long,TuioCursor>(128);
    private Dictionary<long,TuioBlob>   blobList   = new Dictionary<long,TuioBlob>(128);

    public static int width, height;
    private int screen_width  = Screen.PrimaryScreen.Bounds.Width;
    private int screen_height = Screen.PrimaryScreen.Bounds.Height;
    private bool fullscreen, verbose;

    // Navigation
    private AppScreen currentScreen = AppScreen.Explore;
    private int artifactID = -1;

    // Carousel
    private float carouselAngle  = 0f;
    private float carouselTarget = 0f;
    private int   carouselFocused = 0;
    private System.Windows.Forms.Timer carouselTimer;

    // Users / Favourites
    private List<UserRecord> users = new List<UserRecord>();
    private string loggedInName = "";
    private List<int> userFavourites = new List<int>();

    // 3-D
    private Dictionary<int,ObjModel> modelCache = new Dictionary<int,ObjModel>();
    private float artifactAngle = 0f;
    private System.Windows.Forms.Timer rotateTimer;

    // Python socket
    private bool faceDetected = false;

    // Fonts
    private Font fontUI    = new Font("Segoe UI", 11f);
    private Font fontTitle = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
    private Font fontSmall = new Font("Segoe UI", 9f);
    private Font fontTab   = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);

    // Tab hit rects
    private Rectangle tabExplore    = Rectangle.Empty;
    private Rectangle tabFavourites = Rectangle.Empty;
    private Rectangle tabArtifact   = Rectangle.Empty;

    // Status labels
    private Label faceStatusLabel;
    private Label pythonStatusLabel;

    public TuioDemo(int port)
    {
        verbose = false; fullscreen = false;
        width = 1280; height = 720;
        this.ClientSize = new Size(width, height);
        this.Text = "MuseSense - Interactive Museum Guide";
        this.Name = "TuioDemo";
        this.SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.DoubleBuffer, true);
        this.Closing    += new CancelEventHandler(Form_Closing);
        this.KeyDown    += new KeyEventHandler(Form_KeyDown);
        this.Resize     += new EventHandler(Form_Resized);
        this.MouseClick += new MouseEventHandler(Form_MouseClick);
        InitStatusLabels();
        this.WindowState = FormWindowState.Maximized;
        UpdateLayout();

        carouselTimer = new System.Windows.Forms.Timer();
        carouselTimer.Interval = 16;
        carouselTimer.Tick += (s,e) => { carouselAngle += (carouselTarget - carouselAngle) * 0.08f; if (currentScreen==AppScreen.Explore) Invalidate(); };
        carouselTimer.Start();

        rotateTimer = new System.Windows.Forms.Timer();
        rotateTimer.Interval = 16;
        rotateTimer.Tick += (s,e) => { if (currentScreen==AppScreen.Artifact) { artifactAngle += 0.012f; Invalidate(); } };
        rotateTimer.Start();

        LoadUsers();

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        Thread t = new Thread(SocketThread);
        t.IsBackground = true;
        t.Start();
    }

    private void LoadUsers()
    {
        string[] cands = {
            Path.Combine("faces","users.json"),
            Path.Combine(Application.StartupPath,"faces","users.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"faces","users.json"),
            Path.Combine("..","..","faces","users.json"),
            Path.Combine("..","..","..","faces","users.json")
        };
        foreach (string p in cands)
            if (File.Exists(p)) { try { users = TinyJson.ParseUsers(File.ReadAllText(p)); } catch {} break; }
    }

    private void SetLoggedInUser(string name)
    {
        loggedInName = name;
        userFavourites.Clear();
        foreach (UserRecord u in users)
            if (u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            { userFavourites.AddRange(u.Favourites); break; }
        UpdateFaceLabel(true, name);
    }

    private void GoTo(AppScreen screen, int id)
    {
        currentScreen = screen;
        if (id >= 0) artifactID = id;
        if (screen == AppScreen.Artifact && artifactID >= 0) artifactAngle = 0f;
        Invalidate();
    }

    private void GoTo(AppScreen screen) { GoTo(screen, -1); }

    private void Form_MouseClick(object sender, MouseEventArgs e)
    {
        Point p = e.Location;
        if (tabExplore.Contains(p))    { GoTo(AppScreen.Explore);    return; }
        if (tabFavourites.Contains(p)) { GoTo(AppScreen.Favourites); return; }
        if (tabArtifact.Contains(p) && artifactID >= 0) { GoTo(AppScreen.Artifact); return; }

        if (currentScreen == AppScreen.Explore)
            for (int i = 0; i < ArtifactData.Count; i++)
            {
                if (GetCarouselCardRect(i).Contains(p))
                {
                    if (i == carouselFocused) GoTo(AppScreen.Artifact, i);
                    else FocusCard(i);
                    return;
                }
            }

        if (currentScreen == AppScreen.Favourites)
            for (int fi = 0; fi < userFavourites.Count; fi++)
                if (GetFavCardRect(fi).Contains(p)) { GoTo(AppScreen.Artifact, userFavourites[fi]); return; }
    }

    private void FocusCard(int idx)
    {
        carouselFocused = idx;
        float step = (float)(2 * Math.PI / ArtifactData.Count);
        carouselTarget = -idx * step;
    }

    // ── Paint ─────────────────────────────────────────────────────────────────
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        DrawBg(g);
        DrawHeader(g);
        int top = 64;
        switch (currentScreen)
        {
            case AppScreen.Explore:    DrawExplore(g, top);    break;
            case AppScreen.Favourites: DrawFavourites(g, top); break;
            case AppScreen.Artifact:   DrawArtifact(g, top);   break;
        }
    }

    private void DrawBg(Graphics g)
    {
        using (var br = new LinearGradientBrush(new Rectangle(0,0,width,height),
            Color.FromArgb(10,22,48), Color.FromArgb(2,8,22), LinearGradientMode.Vertical))
            g.FillRectangle(br, 0, 0, width, height);
    }

    private void DrawHeader(Graphics g)
    {
        using (var br = new SolidBrush(Color.FromArgb(180,5,15,35)))
            g.FillRectangle(br, 0, 0, width, 60);
        using (var pen = new Pen(Color.FromArgb(80,100,160,220), 1))
            g.DrawLine(pen, 0, 60, width, 60);
        using (var br = new SolidBrush(Color.FromArgb(200,180,220,255)))
            g.DrawString("MuseSense", fontTitle, br, 18, 12);

        string[] labels = { "Explore", "Favourites", "Artifact" };
        AppScreen[] screens = { AppScreen.Explore, AppScreen.Favourites, AppScreen.Artifact };
        int tabW=130, tabH=36, tabY=12;
        int startX = width/2 - (labels.Length*tabW)/2;

        for (int i = 0; i < labels.Length; i++)
        {
            Rectangle r = new Rectangle(startX + i*tabW, tabY, tabW-4, tabH);
            if (i==0) tabExplore=r; else if (i==1) tabFavourites=r; else tabArtifact=r;
            bool active   = (currentScreen == screens[i]);
            bool disabled = (screens[i] == AppScreen.Artifact && artifactID < 0);
            Color bg = active ? Color.FromArgb(200,60,120,200) : Color.FromArgb(80,40,60,100);
            using (var path = RoundRect(r,8)) using (var br = new SolidBrush(bg)) g.FillPath(br, path);
            if (active) using (var pen = new Pen(Color.FromArgb(255,100,180,255),2))
                g.DrawLine(pen, r.Left+8, r.Bottom-1, r.Right-8, r.Bottom-1);
            Color fc = disabled ? Color.FromArgb(80,180,180,180) : Color.White;
            using (var br = new SolidBrush(fc))
            {
                SizeF sz = g.MeasureString(labels[i], fontTab);
                g.DrawString(labels[i], fontTab, br, r.Left+(r.Width-sz.Width)/2, r.Top+(r.Height-sz.Height)/2);
            }
        }
    }

    // ── Explore screen ────────────────────────────────────────────────────────
    private void DrawExplore(Graphics g, int top)
    {
        using (var br = new SolidBrush(Color.FromArgb(200,180,210,255)))
            g.DrawString("Explore Artifacts", fontTitle, br, 20, top+10);
        using (var br = new SolidBrush(Color.FromArgb(140,180,200,220)))
            g.DrawString("Click a card to select  |  Click again to open  |  Place TUIO marker to jump directly",
                fontSmall, br, 20, top+46);

        int cy = top + 80 + (height - top - 80) / 2;
        float step = (float)(2 * Math.PI / ArtifactData.Count);
        int[] order = BackToFrontOrder(carouselFocused, ArtifactData.Count);
        foreach (int i in order) DrawCarouselCard(g, i, carouselAngle + i*step, cy);

        using (var br = new SolidBrush(Color.FromArgb(100,160,180,200)))
            g.DrawString("Arrow keys or click to rotate", fontSmall, br, width/2-100, height-28);
    }

    private int[] BackToFrontOrder(int focused, int count)
    {
        var order = new List<int>();
        for (int i = 0; i < count; i++) if (i != focused) order.Add(i);
        order.Add(focused);
        return order.ToArray();
    }

    private void DrawCarouselCard(Graphics g, int idx, float angle, int cy)
    {
        float radius = width * 0.28f;
        float x = (float)Math.Sin(angle) * radius;
        float z = (float)Math.Cos(angle);
        float scale = 0.55f + 0.45f * ((z + 1f) / 2f);
        int cardW = (int)(260 * scale);
        int cardH = (int)(320 * scale);
        int cx = width/2 + (int)x;
        int cardTop = cy - cardH/2;
        int alpha = (int)(80 + 175 * ((z + 1f) / 2f));
        bool isFocused = (idx == carouselFocused);
        Rectangle r = new Rectangle(cx - cardW/2, cardTop, cardW, cardH);

        using (var path = RoundRect(r,14)) using (var br = new SolidBrush(Color.FromArgb(alpha,20,40,80))) g.FillPath(br, path);
        Color border = isFocused ? Color.FromArgb(alpha,100,180,255) : Color.FromArgb(alpha/2,60,100,160);
        using (var path = RoundRect(r,14)) using (var pen = new Pen(border, isFocused?2.5f:1f)) g.DrawPath(pen, path);

        ObjModel model = TryGetModel(idx);
        if (model != null)
        {
            int ms = (int)(cardW * 0.72f);
            float spin = (float)(DateTime.Now.TimeOfDay.TotalSeconds * 0.5) + idx * 2.1f;
            RenderObjModel(g, model, cx, cardTop + cardH/3, spin, ms, ArtifactData.Colors[idx]);
        }

        using (var br = new SolidBrush(Color.FromArgb(alpha,230,230,255)))
        {
            Font f = isFocused ? fontTab : fontSmall;
            SizeF sz = g.MeasureString(ArtifactData.Names[idx], f);
            g.DrawString(ArtifactData.Names[idx], f, br, cx - sz.Width/2, cardTop + cardH - sz.Height*2.2f);
        }
        if (isFocused) using (var br = new SolidBrush(Color.FromArgb(180,100,200,255)))
        {
            SizeF sz = g.MeasureString("Click to open", fontSmall);
            g.DrawString("Click to open", fontSmall, br, cx - sz.Width/2, cardTop + cardH - sz.Height*1.1f);
        }
    }

    private Rectangle GetCarouselCardRect(int idx)
    {
        float step = (float)(2 * Math.PI / ArtifactData.Count);
        float angle = carouselAngle + idx * step;
        float x = (float)Math.Sin(angle) * width * 0.28f;
        float z = (float)Math.Cos(angle);
        float scale = 0.55f + 0.45f * ((z + 1f) / 2f);
        int cardW = (int)(260 * scale), cardH = (int)(320 * scale);
        int cy = 64 + 80 + (height - 64 - 80) / 2;
        int cx = width/2 + (int)x;
        return new Rectangle(cx - cardW/2, cy - cardH/2, cardW, cardH);
    }

    // ── Favourites screen ─────────────────────────────────────────────────────
    private void DrawFavourites(Graphics g, int top)
    {
        using (var br = new SolidBrush(Color.FromArgb(200,180,210,255)))
            g.DrawString("Favourites", fontTitle, br, 20, top+10);

        if (string.IsNullOrEmpty(loggedInName))
        {
            using (var br = new SolidBrush(Color.FromArgb(160,200,200,200)))
                g.DrawString("No user logged in. Face detection will load your favourites.", fontUI, br, 20, top+60);
            return;
        }
        using (var br = new SolidBrush(Color.FromArgb(160,160,200,160)))
            g.DrawString("Logged in as: " + loggedInName, fontSmall, br, 20, top+46);

        if (userFavourites.Count == 0)
        {
            using (var br = new SolidBrush(Color.FromArgb(140,180,180,180)))
                g.DrawString("No favourites saved yet.", fontUI, br, 20, top+80);
            return;
        }
        for (int fi = 0; fi < userFavourites.Count; fi++)
            DrawFavCard(g, GetFavCardRect(fi), userFavourites[fi]);
    }

    private Rectangle GetFavCardRect(int fi)
    {
        int cardW=220, cardH=280, margin=30, top=64+80;
        int totalW = userFavourites.Count * (cardW + margin) - margin;
        int startX = Math.Max(20, width/2 - totalW/2);
        return new Rectangle(startX + fi*(cardW+margin), top, cardW, cardH);
    }

    private void DrawFavCard(Graphics g, Rectangle r, int id)
    {
        using (var path = RoundRect(r,14)) using (var br = new SolidBrush(Color.FromArgb(160,20,40,80))) g.FillPath(br, path);
        using (var path = RoundRect(r,14)) using (var pen = new Pen(Color.FromArgb(160,255,180,60),2f)) g.DrawPath(pen, path);
        using (var br = new SolidBrush(Color.FromArgb(220,255,200,50))) g.DrawString("*", fontTitle, br, r.Left+8, r.Top+6);

        ObjModel model = TryGetModel(id);
        if (model != null)
        {
            int ms = (int)(r.Width * 0.65f);
            float spin = (float)(DateTime.Now.TimeOfDay.TotalSeconds * 0.4) + id * 1.5f;
            RenderObjModel(g, model, r.Left+r.Width/2, r.Top+r.Height/3, spin, ms, ArtifactData.Colors[id]);
        }
        using (var br = new SolidBrush(Color.White))
        {
            SizeF sz = g.MeasureString(ArtifactData.Names[id], fontSmall);
            g.DrawString(ArtifactData.Names[id], fontSmall, br, r.Left+(r.Width-sz.Width)/2, r.Bottom-sz.Height*2.2f);
        }
        using (var br = new SolidBrush(Color.FromArgb(160,100,200,255)))
        {
            SizeF sz = g.MeasureString("Tap to view", fontSmall);
            g.DrawString("Tap to view", fontSmall, br, r.Left+(r.Width-sz.Width)/2, r.Bottom-sz.Height*1.1f);
        }
    }

    // ── Artifact detail screen ────────────────────────────────────────────────
    private void DrawArtifact(Graphics g, int top)
    {
        if (artifactID < 0 || artifactID >= ArtifactData.Count)
        {
            using (var br = new SolidBrush(Color.White)) g.DrawString("No artifact selected.", fontUI, br, 20, top+20);
            return;
        }
        int modelSize = (int)(Math.Min(width * 0.45f, height - top - 40));
        int mx = modelSize/2 + 40;
        int my = top + (height - top) / 2;

        ObjModel model = TryGetModel(artifactID);
        if (model != null)
        {
            using (var br = new SolidBrush(Color.FromArgb(30,100,160,255)))
            { int gs=modelSize+60; g.FillEllipse(br, mx-gs/2, my-gs/2, gs, gs); }
            RenderObjModel(g, model, mx, my, artifactAngle, modelSize, ArtifactData.Colors[artifactID]);
        }

        int rx = mx + modelSize/2 + 40;
        int ry = top + 30;
        int rw = width - rx - 20;

        using (var br = new SolidBrush(Color.FromArgb(255,220,200,100)))
            g.DrawString(ArtifactData.Names[artifactID], fontTitle, br, rx, ry);
        using (var pen = new Pen(Color.FromArgb(100,100,160,220),1))
            g.DrawLine(pen, rx, ry+44, rx+rw, ry+44);
        using (var br = new SolidBrush(Color.FromArgb(200,200,210,230)))
            g.DrawString(ArtifactData.Descriptions[artifactID], fontUI, br, new RectangleF(rx, ry+56, rw, 200));
        using (var br = new SolidBrush(Color.FromArgb(120,140,200,140)))
            g.DrawString("Rotate the TUIO marker to spin the model", fontSmall, br, rx, ry+270);

        bool isFav = userFavourites.Contains(artifactID);
        using (var br = new SolidBrush(isFav ? Color.FromArgb(220,255,200,50) : Color.FromArgb(80,180,180,180)))
            g.DrawString(isFav ? "* In your Favourites" : "Not in Favourites", fontSmall, br, rx, ry+300);
        using (var br = new SolidBrush(Color.FromArgb(100,100,160,200)))
            g.DrawString("Marker ID: " + artifactID, fontSmall, br, rx, ry+325);
    }

    // ── 3-D rendering ─────────────────────────────────────────────────────────
    private ObjModel TryGetModel(int id)
    {
        if (modelCache.ContainsKey(id)) return modelCache[id];
        ObjModel m = LoadObjModel(ArtifactData.GetObjPath(id));
        modelCache[id] = m;
        return m;
    }

    private void RenderObjModel(Graphics g, ObjModel model, int cx, int cy, float angle, int size, Color baseColor)
    {
        if (model == null) return;
        int W=size, H=size;
        int[] pixels = new int[W*H];
        float[] zBuf = new float[W*H];
        for (int i=0;i<zBuf.Length;i++) zBuf[i]=float.NegativeInfinity;
        float scale=(size*0.95f)/model.Radius, yaw=angle, pitch=-0.35f, camZ=size*3.2f, proj=size*1.7f;
        Vec3[] trans=new Vec3[model.Verts.Count]; PointF[] proj2=new PointF[model.Verts.Count]; float[] depth=new float[model.Verts.Count];
        for (int i=0;i<model.Verts.Count;i++)
        {
            Vec3 tv=RotV(model.Verts[i],yaw,pitch); tv.X*=scale;tv.Y*=scale;tv.Z*=scale; trans[i]=tv;
            float z=tv.Z+camZ; if(z<1f)z=1f; depth[i]=tv.Z;
            proj2[i]=new PointF(W*0.5f+tv.X*proj/z, H*0.5f-tv.Y*proj/z);
        }
        Vec3 ld=Norm(new Vec3(0.2f,-0.5f,1f));
        for (int i=0;i<model.Faces.Count;i++)
        {
            Face3 f=model.Faces[i]; Vec3 a=trans[f.A],b=trans[f.B],c=trans[f.C];
            float nx=(b.Y-a.Y)*(c.Z-a.Z)-(b.Z-a.Z)*(c.Y-a.Y);
            float ny=(b.Z-a.Z)*(c.X-a.X)-(b.X-a.X)*(c.Z-a.Z);
            float nz=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            float nl=(float)Math.Sqrt(nx*nx+ny*ny+nz*nz); if(nl<1e-5f)continue;
            nx/=nl;ny/=nl;nz/=nl;
            float lf=Math.Min(1.18f,0.68f+0.42f*Math.Abs(nx*ld.X+ny*ld.Y+nz*ld.Z));
            Color fc=(!string.IsNullOrEmpty(f.Mat)&&model.MatColors.ContainsKey(f.Mat))?model.MatColors[f.Mat]:baseColor;
            Bitmap tex=(!string.IsNullOrEmpty(f.Mat)&&model.MatTextures.ContainsKey(f.Mat))?model.MatTextures[f.Mat]:null;
            Vec2 uv1=(f.TA>=0&&f.TA<model.UVs.Count)?model.UVs[f.TA]:null;
            Vec2 uv2=(f.TB>=0&&f.TB<model.UVs.Count)?model.UVs[f.TB]:null;
            Vec2 uv3=(f.TC>=0&&f.TC<model.UVs.Count)?model.UVs[f.TC]:null;
            RasterTri(pixels,zBuf,W,H,proj2[f.A],proj2[f.B],proj2[f.C],depth[f.A],depth[f.B],depth[f.C],uv1,uv2,uv3,tex,fc,lf);
        }
        using (Bitmap bmp=new Bitmap(W,H,PixelFormat.Format32bppArgb))
        {
            BitmapData bd=bmp.LockBits(new Rectangle(0,0,W,H),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels,0,bd.Scan0,pixels.Length); bmp.UnlockBits(bd);
            g.DrawImage(bmp,cx-W/2,cy-H/2,W,H);
        }
    }

    private Vec3 RotV(Vec3 v,float yaw,float pitch)
    {
        float cy=(float)Math.Cos(yaw),sy=(float)Math.Sin(yaw),cp=(float)Math.Cos(pitch),sp=(float)Math.Sin(pitch);
        float x1=v.X*cy+v.Z*sy,z1=-v.X*sy+v.Z*cy,y1=v.Y;
        return new Vec3(x1,y1*cp-z1*sp,y1*sp+z1*cp);
    }
    private Vec3 Norm(Vec3 v){float l=(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);return l<1e-5f?v:new Vec3(v.X/l,v.Y/l,v.Z/l);}

    private void RasterTri(int[] px,float[] zb,int W,int H,PointF p1,PointF p2,PointF p3,float z1,float z2,float z3,Vec2 uv1,Vec2 uv2,Vec2 uv3,Bitmap tex,Color fc,float lf)
    {
        int x0=(int)Math.Max(0,Math.Floor(Math.Min(p1.X,Math.Min(p2.X,p3.X))));
        int x1=(int)Math.Min(W-1,Math.Ceiling(Math.Max(p1.X,Math.Max(p2.X,p3.X))));
        int y0=(int)Math.Max(0,Math.Floor(Math.Min(p1.Y,Math.Min(p2.Y,p3.Y))));
        int y1=(int)Math.Min(H-1,Math.Ceiling(Math.Max(p1.Y,Math.Max(p2.Y,p3.Y))));
        float denom=((p2.Y-p3.Y)*(p1.X-p3.X)+(p3.X-p2.X)*(p1.Y-p3.Y));
        if(Math.Abs(denom)<1e-5f)return;
        bool useTex=tex!=null&&uv1!=null&&uv2!=null&&uv3!=null;
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
        {
            float w1=((p2.Y-p3.Y)*(x-p3.X)+(p3.X-p2.X)*(y-p3.Y))/denom;
            float w2=((p3.Y-p1.Y)*(x-p3.X)+(p1.X-p3.X)*(y-p3.Y))/denom;
            float w3=1f-w1-w2;
            if(w1<0||w2<0||w3<0)continue;
            float z=w1*z1+w2*z2+w3*z3; int idx=y*W+x;
            if(z<=zb[idx])continue; zb[idx]=z;
            Color sc=fc;
            if(useTex){float u=w1*uv1.U+w2*uv2.U+w3*uv3.U,v=w1*uv1.V+w2*uv2.V+w3*uv3.V;
                u=Math.Max(0f,Math.Min(1f,u));v=Math.Max(0f,Math.Min(1f,v));
                sc=tex.GetPixel(Math.Max(0,Math.Min(tex.Width-1,(int)(u*(tex.Width-1)))),Math.Max(0,Math.Min(tex.Height-1,(int)((1f-v)*(tex.Height-1)))));}
            float exp=1.12f;
            px[idx]=Color.FromArgb(255,Math.Max(0,Math.Min(255,(int)(sc.R*lf*exp+8))),Math.Max(0,Math.Min(255,(int)(sc.G*lf*exp+8))),Math.Max(0,Math.Min(255,(int)(sc.B*lf*exp+8)))).ToArgb();
        }
    }

    // ── OBJ / MTL loader ──────────────────────────────────────────────────────
    private ObjModel LoadObjModel(string rel)
    {
        string path=Resolve(rel); if(string.IsNullOrEmpty(path))return null;
        ObjModel m=new ObjModel(); string dir=Path.GetDirectoryName(path);
        List<string> mtlLibs=new List<string>(); string curMat=null;
        foreach(string raw in File.ReadAllLines(path))
        {
            if(string.IsNullOrWhiteSpace(raw))continue; string line=raw.Trim();
            if(line.StartsWith("mtllib ")){mtlLibs.Add(line.Substring(7).Trim());continue;}
            if(line.StartsWith("usemtl ")){curMat=line.Substring(7).Trim();continue;}
            if(line.StartsWith("v "))
            {
                string[] p=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                if(p.Length>=4){float x,y,z; if(TryF(p[1],out x)&&TryF(p[2],out y)&&TryF(p[3],out z))m.Verts.Add(new Vec3(x,y,z));}
            }
            else if(line.StartsWith("vt "))
            {
                string[] p=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                if(p.Length>=3){float u,v; if(TryF(p[1],out u)&&TryF(p[2],out v))m.UVs.Add(new Vec2(u,v));}
            }
            else if(line.StartsWith("f "))
            {
                string[] p=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                if(p.Length>=4){
                    int[] vi=new int[p.Length-1];int[] ti=new int[p.Length-1];bool ok=true;
                    for(int i=1;i<p.Length;i++){int iv=ObjIdx(p[i],m.Verts.Count),it=ObjTexIdx(p[i],m.UVs.Count);if(iv<0||iv>=m.Verts.Count){ok=false;break;}vi[i-1]=iv;ti[i-1]=it;}
                    if(!ok)continue;
                    for(int i=1;i<vi.Length-1;i++)m.Faces.Add(new Face3(vi[0],vi[i],vi[i+1],ti[0],ti[i],ti[i+1],curMat));
                }
            }
        }
        var mats=LoadMtls(dir,mtlLibs);
        foreach(var kv in mats){Color fc=kv.Value.Diffuse;if(!string.IsNullOrEmpty(kv.Value.TexPath)&&File.Exists(kv.Value.TexPath))fc=AvgColor(kv.Value.TexPath,fc);m.MatColors[kv.Key]=fc;if(kv.Value.TexBmp!=null)m.MatTextures[kv.Key]=kv.Value.TexBmp;}
        if(m.Verts.Count==0||m.Faces.Count==0)return null;
        float cx=0,cy=0,cz=0; foreach(var v in m.Verts){cx+=v.X;cy+=v.Y;cz+=v.Z;} cx/=m.Verts.Count;cy/=m.Verts.Count;cz/=m.Verts.Count;
        float maxR=1e-4f; foreach(var v in m.Verts){v.X-=cx;v.Y-=cy;v.Z-=cz;float r=(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);if(r>maxR)maxR=r;} m.Radius=maxR;
        return m;
    }

    private Dictionary<string,MaterialInfo> LoadMtls(string dir,List<string> libs)
    {
        var res=new Dictionary<string,MaterialInfo>();
        foreach(string lib in libs){string p=Path.Combine(dir,lib);if(!File.Exists(p))continue;string mdir=Path.GetDirectoryName(p);string cur=null;
            foreach(string raw in File.ReadAllLines(p)){if(string.IsNullOrWhiteSpace(raw))continue;string line=raw.Trim();if(line.StartsWith("#"))continue;
                if(line.StartsWith("newmtl ")){cur=line.Substring(7).Trim();if(!res.ContainsKey(cur))res[cur]=new MaterialInfo();continue;}
                if(string.IsNullOrEmpty(cur)||!res.ContainsKey(cur))continue;
                if(line.StartsWith("Kd ")){string[] pts=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(pts.Length>=4){float r,gg,b;if(TryF(pts[1],out r)&&TryF(pts[2],out gg)&&TryF(pts[3],out b))res[cur].Diffuse=Color.FromArgb(220,(int)(r*255),(int)(gg*255),(int)(b*255));}}
                if(line.StartsWith("map_Kd ")){string tp=line.Substring(7).Trim();if(!string.IsNullOrEmpty(tp))res[cur].TexPath=Path.Combine(mdir,tp);}
            }
            foreach(var kv in res)if(!string.IsNullOrEmpty(kv.Value.TexPath)&&File.Exists(kv.Value.TexPath))try{kv.Value.TexBmp=new Bitmap(kv.Value.TexPath);}catch{}
        }
        return res;
    }

    private bool TryF(string s,out float v){return float.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v);}
    private int ObjIdx(string tok,int cnt){int i;if(!int.TryParse(tok.Split('/')[0],out i))return -1;return i>0?i-1:i<0?cnt+i:-1;}
    private int ObjTexIdx(string tok,int cnt){string[] c=tok.Split('/');if(c.Length<2||string.IsNullOrEmpty(c[1]))return -1;int i;if(!int.TryParse(c[1],out i))return -1;return i>0?i-1:i<0?cnt+i:-1;}
    private string Resolve(string rel){if(string.IsNullOrEmpty(rel))return null;string[] cands={rel,Path.Combine(Application.StartupPath,rel),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,rel)};foreach(string c in cands)if(File.Exists(c))return c;return null;}
    private Color AvgColor(string imgPath,Color fallback){try{using(Bitmap bmp=new Bitmap(imgPath)){int sx=Math.Max(1,bmp.Width/40),sy=Math.Max(1,bmp.Height/40);long sr=0,sg=0,sb=0,cnt=0;for(int y=0;y<bmp.Height;y+=sy)for(int x=0;x<bmp.Width;x+=sx){Color c=bmp.GetPixel(x,y);sr+=c.R;sg+=c.G;sb+=c.B;cnt++;}if(cnt==0)return fallback;return Color.FromArgb(220,(int)(sr/cnt),(int)(sg/cnt),(int)(sb/cnt));}}catch{return fallback;}}

    // ── TUIO callbacks ────────────────────────────────────────────────────────
    public void addTuioObject(TuioObject o)
    {
        lock(objectList) objectList[o.SessionID]=o;
        if(verbose) Console.WriteLine("add obj "+o.SymbolID);
        if(o.SymbolID>=0&&o.SymbolID<ArtifactData.Count)
            this.BeginInvoke(new MethodInvoker(()=>{ artifactAngle=(float)o.Angle; GoTo(AppScreen.Artifact,o.SymbolID); }));
    }
    public void updateTuioObject(TuioObject o)
    {
        lock(objectList) if(objectList.ContainsKey(o.SessionID)) objectList[o.SessionID]=o;
        if(currentScreen==AppScreen.Artifact&&o.SymbolID==artifactID) artifactAngle=(float)o.Angle;
    }
    public void removeTuioObject(TuioObject o){lock(objectList)objectList.Remove(o.SessionID);}
    public void addTuioCursor(TuioCursor c){lock(cursorList)cursorList[c.SessionID]=c;}
    public void updateTuioCursor(TuioCursor c){}
    public void removeTuioCursor(TuioCursor c){lock(cursorList)cursorList.Remove(c.SessionID);}
    public void addTuioBlob(TuioBlob b){lock(blobList)blobList[b.SessionID]=b;}
    public void updateTuioBlob(TuioBlob b){}
    public void removeTuioBlob(TuioBlob b){lock(blobList)blobList.Remove(b.SessionID);}
    public void refresh(TuioTime t){Invalidate();}

    // ── Python socket ─────────────────────────────────────────────────────────
    private void SocketThread()
    {
        while(true)
        {
            TcpClient tcp=null; NetworkStream ns=null;
            try
            {
                tcp=new TcpClient("localhost",5000); ns=tcp.GetStream();
                SetPyStatus(true);
                byte[] buf=new byte[1024];
                while(true){int n=ns.Read(buf,0,buf.Length);if(n<=0)break;HandleMsg(Encoding.UTF8.GetString(buf,0,n).Trim());}
            }
            catch{}
            finally{if(ns!=null)try{ns.Close();}catch{}if(tcp!=null)try{tcp.Close();}catch{}}
            SetPyStatus(false);
            Thread.Sleep(2000);
        }
    }

    private void HandleMsg(string msg)
    {
        if(msg.Contains("face:detected:"))
        {
            string name=msg.Substring(msg.IndexOf("face:detected:")+"face:detected:".Length).Trim();
            faceDetected=true;
            this.BeginInvoke(new MethodInvoker(()=>SetLoggedInUser(name)));
        }
        else if(msg.Contains("face:detected")||msg.Contains("face detected")){faceDetected=true;UpdateFaceLabel(true,"");}
        else if(msg.Contains("face:lost")||msg.Contains("face lost")){faceDetected=false;UpdateFaceLabel(false,"");}
    }

    private void SetPyStatus(bool connected)
    {
        string txt=connected?"Connected":"Disconnected"; Color col=connected?Color.LimeGreen:Color.OrangeRed;
        if(pythonStatusLabel.InvokeRequired) pythonStatusLabel.BeginInvoke(new MethodInvoker(()=>{pythonStatusLabel.Text="Python: "+txt;pythonStatusLabel.ForeColor=col;}));
        else{pythonStatusLabel.Text="Python: "+txt;pythonStatusLabel.ForeColor=col;}
    }

    private void UpdateFaceLabel(bool detected,string name)
    {
        string txt=detected?(string.IsNullOrEmpty(name)?"Face Detected":"Logged in: "+name):"Face: Not Detected";
        Color col=detected?Color.LightGreen:Color.LightCoral;
        if(faceStatusLabel.InvokeRequired) faceStatusLabel.BeginInvoke(new MethodInvoker(()=>{faceStatusLabel.Text=txt;faceStatusLabel.ForeColor=col;}));
        else{faceStatusLabel.Text=txt;faceStatusLabel.ForeColor=col;}
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
    private void InitStatusLabels()
    {
        faceStatusLabel=new Label{AutoSize=false,Size=new Size(260,30),Location=new Point(width-270,10),
            BackColor=Color.Transparent,ForeColor=Color.LightCoral,Font=new Font("Segoe UI Semibold",12f,FontStyle.Bold),
            TextAlign=ContentAlignment.MiddleRight,Text="Face: Not Detected"};
        this.Controls.Add(faceStatusLabel);
        pythonStatusLabel=new Label{AutoSize=false,Size=new Size(260,24),Location=new Point(width-270,42),
            BackColor=Color.Transparent,ForeColor=Color.OrangeRed,Font=new Font("Segoe UI",9f),
            TextAlign=ContentAlignment.MiddleRight,Text="Python: Disconnected"};
        this.Controls.Add(pythonStatusLabel);
    }

    private void UpdateLayout()
    {
        width=this.ClientSize.Width; height=this.ClientSize.Height;
        if(faceStatusLabel!=null)  faceStatusLabel.Location=new Point(width-270,10);
        if(pythonStatusLabel!=null) pythonStatusLabel.Location=new Point(width-270,42);
    }

    private GraphicsPath RoundRect(Rectangle r,int radius)
    {
        var path=new GraphicsPath(); int d=radius*2;
        path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        path.CloseFigure(); return path;
    }

    // ── Form events ───────────────────────────────────────────────────────────
    private void Form_Resized(object sender,EventArgs e){UpdateLayout();}

    private void Form_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.KeyCode==Keys.F1)
        {
            if(!fullscreen){this.FormBorderStyle=FormBorderStyle.None;this.Left=0;this.Top=0;this.Width=screen_width;this.Height=screen_height;fullscreen=true;}
            else{this.FormBorderStyle=FormBorderStyle.Sizable;this.Width=1280;this.Height=720;fullscreen=false;}
        }
        else if(e.KeyCode==Keys.Escape) this.Close();
        else if(e.KeyCode==Keys.V) verbose=!verbose;
        else if(e.KeyCode==Keys.Left&&currentScreen==AppScreen.Explore){carouselFocused=(carouselFocused-1+ArtifactData.Count)%ArtifactData.Count;FocusCard(carouselFocused);}
        else if(e.KeyCode==Keys.Right&&currentScreen==AppScreen.Explore){carouselFocused=(carouselFocused+1)%ArtifactData.Count;FocusCard(carouselFocused);}
    }

    private void Form_Closing(object sender,CancelEventArgs e)
    {
        foreach(var m in modelCache.Values) if(m!=null) foreach(var t in m.MatTextures.Values) if(t!=null) t.Dispose();
        client.removeTuioListener(this); client.disconnect();
        System.Environment.Exit(0);
    }

    public static void Main(string[] argv)
    {
        int port = argv.Length==1 ? int.Parse(argv[0]) : 3333;
        Application.Run(new TuioDemo(port));
    }
}
