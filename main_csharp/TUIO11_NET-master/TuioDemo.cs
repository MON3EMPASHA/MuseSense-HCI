// MuseSense - Interactive Museum Guide
// Multi-screen: Explore | Favourites | Artifact Detail
// TUIO marker detection auto-navigates to Artifact page.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;
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
            var favs = new List<string>();
            int fi = json.IndexOf("\"favourites\"", q2);
            int nextName = json.IndexOf("\"name\"", q2 + 1);
            if (fi >= 0 && (nextName < 0 || fi < nextName))
            {
                int arrStart = json.IndexOf('[', fi);
                int arrEnd   = json.IndexOf(']', arrStart);
                string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
                if (!string.IsNullOrWhiteSpace(inner))
                    favs.AddRange(ParseFavouriteNames(inner));
            }
            result.Add(new UserRecord { Name = name, Favourites = favs });
            pos = q2 + 1;
        }
        return result;
    }

    private static List<string> ParseFavouriteNames(string inner)
    {
        var result = new List<string>();
        int pos = 0;

        while (pos < inner.Length)
        {
            while (pos < inner.Length && (char.IsWhiteSpace(inner[pos]) || inner[pos] == ',')) pos++;
            if (pos >= inner.Length) break;

            if (inner[pos] == '"')
            {
                int end = pos + 1;
                while (end < inner.Length)
                {
                    if (inner[end] == '"' && inner[end - 1] != '\\') break;
                    end++;
                }

                if (end < inner.Length)
                {
                    string name = inner.Substring(pos + 1, end - pos - 1);
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                    pos = end + 1;
                    continue;
                }
            }

            int tokenEnd = pos;
            while (tokenEnd < inner.Length && inner[tokenEnd] != ',') tokenEnd++;
            string token = inner.Substring(pos, tokenEnd - pos).Trim();
            int favouriteId;
            if (int.TryParse(token, out favouriteId) && favouriteId >= 0 && favouriteId < ArtifactData.Count)
                result.Add(ArtifactData.Names[favouriteId]);
            pos = tokenEnd + 1;
        }

        return result;
    }
}

class UserRecord
{
    public string Name;
    public List<string> Favourites = new List<string>();
}

// Artifact data
static class ArtifactData
{
    class ArtifactRecord
    {
        public int id;
        public int tuioId;
        public string name;
        public string birthDate;
        public string era;
        public string origin;
        public string description;
        public string narration;
        public string objPath;
        public string audioPath;
        public string color;
    }

    class ArtifactRoot
    {
        public List<ArtifactRecord> artifacts;
    }

    private static List<ArtifactRecord> records = new List<ArtifactRecord>();
    private static Dictionary<int, int> tuioToIndex = new Dictionary<int, int>();

    public static string[] Names = new string[0];
    public static string[] Descriptions = new string[0];
    public static string[] Narrations = new string[0];
    public static string[] BirthDates = new string[0];
    public static string[] Eras = new string[0];
    public static string[] Origins = new string[0];
    public static string[] AudioPaths = new string[0];
    public static Color[] Colors = new Color[0];

    static ArtifactData()
    {
        Load();
    }

    private static void Load()
    {
        string[] cands = {
            Path.Combine("artifacts.json"),
            Path.Combine(Application.StartupPath, "artifacts.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts.json"),
            Path.Combine("..", "..", "artifacts.json"),
            Path.Combine("..", "..", "..", "artifacts.json")
        };

        foreach (string p in cands)
        {
            if (!File.Exists(p)) continue;
            try
            {
                string json = File.ReadAllText(p);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ArtifactRoot root = ser.Deserialize<ArtifactRoot>(json);
                if (root != null && root.artifacts != null && root.artifacts.Count > 0)
                {
                    records = root.artifacts;
                    BuildCaches();
                    return;
                }
            }
            catch { }
        }

        LoadFallback();
        BuildCaches();
    }

    private static void LoadFallback()
    {
        records = new List<ArtifactRecord>
        {
            new ArtifactRecord {
                id=0, tuioId=0, name="Mask of Tutankhamun", birthDate="c. 1323 BC", era="New Kingdom",
                origin="Valley of the Kings", description="Golden funerary mask of Pharaoh Tutankhamun, crafted with gold and semi-precious inlays.",
                narration="You are viewing the mask of Tutankhamun, one of Ancient Egypt's most iconic royal artifacts.",
                objPath=Path.Combine("3d models","Mask of Tutankhamun","Mask of Tutankhamun.obj"), audioPath=Path.Combine("audio","tutankhamun.wav"), color="#BFAA2A"
            },
            new ArtifactRecord {
                id=1, tuioId=1, name="Ramses II Statue", birthDate="1279-1213 BC", era="New Kingdom",
                origin="Grand Egyptian Museum", description="Monumental granite statue representing Ramses II, one of Egypt's most influential pharaohs.",
                narration="This colossal statue represents Ramses the Great, famous for military power and monumental architecture.",
                objPath=Path.Combine("3d models","Ramses II statue at the Grand Egyptian Museum","Ramses II statue at the Grand Egyptian Museum .obj"), audioPath=Path.Combine("audio","ramses_ii.wav"), color="#A0A0A0"
            },
            new ArtifactRecord {
                id=2, tuioId=2, name="King Senwosret III", birthDate="1836-1818 BC", era="Middle Kingdom",
                origin="Ancient Egypt", description="Quartzite portrait with a realistic expression often interpreted as royal responsibility and endurance.",
                narration="Senwosret the Third is remembered for state reforms and strong military campaigns in the Middle Kingdom.",
                objPath=Path.Combine("3d models","King Senwosret III (1836-1818 BC)","King Senwosret III (1836-1818 BC).obj"), audioPath=Path.Combine("audio","senwosret_iii.wav"), color="#CDAA7D"
            },
            new ArtifactRecord {
                id=3, tuioId=3, name="Nefertiti Bust", birthDate="c. 1345 BC", era="Amarna Period",
                origin="Akhetaten", description="A renowned portrait of Queen Nefertiti, celebrated for balanced proportions and elegant facial features.",
                narration="The bust of Nefertiti is one of the most recognized symbols of artistry in Ancient Egypt.",
                objPath=Path.Combine("3d models","Mask of Tutankhamun","Mask of Tutankhamun.obj"), audioPath=Path.Combine("audio","nefertiti.wav"), color="#C58B6D"
            }
        };
    }

    private static void BuildCaches()
    {
        records.Sort((a, b) => a.id.CompareTo(b.id));
        Names = new string[records.Count];
        Descriptions = new string[records.Count];
        Narrations = new string[records.Count];
        BirthDates = new string[records.Count];
        Eras = new string[records.Count];
        Origins = new string[records.Count];
        AudioPaths = new string[records.Count];
        Colors = new Color[records.Count];
        tuioToIndex.Clear();

        for (int i = 0; i < records.Count; i++)
        {
            ArtifactRecord r = records[i];
            Names[i] = string.IsNullOrEmpty(r.name) ? ("Artifact " + r.id) : r.name;
            Descriptions[i] = string.IsNullOrEmpty(r.description) ? "No description available." : r.description;
            Narrations[i] = string.IsNullOrEmpty(r.narration) ? "No narration available." : r.narration;
            BirthDates[i] = string.IsNullOrEmpty(r.birthDate) ? "Unknown" : r.birthDate;
            Eras[i] = string.IsNullOrEmpty(r.era) ? "Unknown" : r.era;
            Origins[i] = string.IsNullOrEmpty(r.origin) ? "Unknown" : r.origin;
            AudioPaths[i] = string.IsNullOrEmpty(r.audioPath) ? "" : r.audioPath;
            Colors[i] = ParseColor(r.color, Color.FromArgb(220, 180, 180, 180));
            if (!tuioToIndex.ContainsKey(r.tuioId)) tuioToIndex.Add(r.tuioId, i);
        }
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        string h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return fallback;
        try
        {
            int r = Convert.ToInt32(h.Substring(0, 2), 16);
            int g = Convert.ToInt32(h.Substring(2, 2), 16);
            int b = Convert.ToInt32(h.Substring(4, 2), 16);
            return Color.FromArgb(220, r, g, b);
        }
        catch { return fallback; }
    }

    public static int GetIndexByTuioId(int tuioId)
    {
        int idx;
        return tuioToIndex.TryGetValue(tuioId, out idx) ? idx : -1;
    }

    public static string GetObjPath(int id)
    {
        if (id < 0 || id >= records.Count) return null;
        return records[id].objPath;
    }

    public static string GetAudioPath(int id)
    {
        if (id < 0 || id >= records.Count) return null;
        return AudioPaths[id];
    }

    public static int Count { get { return Names.Length; } }
}

enum AppScreen { Explore, Favourites, Artifact }

// 3D geometry helpers
class Vec3 { public float X,Y,Z; public Vec3(float x,float y,float z){X=x;Y=y;Z=z;} }
class Vec2 { public float U,V; public Vec2(float u,float v){U=u;V=v;} }
class Face3 {
    public int A,B,C,TA,TB,TC; public string Mat;
    public Face3(int a,int b,int c,int ta,int tb,int tc,string m){A=a;B=b;C=c;TA=ta;TB=tb;TC=tc;Mat=m;}
}
class MaterialInfo { public Color Diffuse=Color.LightGray; public string TexPath; public Bitmap TexBmp; }
class TextureData { public int Width; public int Height; public int[] Pixels; }
class ObjModel {
    public List<Vec3> Verts=new List<Vec3>();
    public List<Vec2> UVs=new List<Vec2>();
    public List<Face3> Faces=new List<Face3>();
    public Dictionary<string,Color> MatColors=new Dictionary<string,Color>();
    public Dictionary<string,TextureData> MatTextures=new Dictionary<string,TextureData>();
    public float Radius=1f;
}

class ThemedPanel : Panel
{
    public int Radius = 12;
    public Color Border = Color.FromArgb(150, 90, 150, 215);
    public Color FillTop = Color.FromArgb(160, 20, 38, 72);
    public Color FillBottom = Color.FromArgb(155, 8, 18, 36);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath gp = RoundRectPath(r, Radius))
        using (LinearGradientBrush br = new LinearGradientBrush(r, FillTop, FillBottom, LinearGradientMode.Vertical))
        using (Pen pen = new Pen(Border, 1.2f))
        {
            e.Graphics.FillPath(br, gp);
            e.Graphics.DrawPath(pen, gp);
        }
    }

    private GraphicsPath RoundRectPath(Rectangle r, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

static class MciAudio
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder buffer, int bufferSize, IntPtr hwndCallback);

    // waveOutSetVolume sets left+right channel volume on the default wave output device.
    // volume is a DWORD: high word = right channel, low word = left channel, range 0x0000–0xFFFF.
    [DllImport("winmm.dll")]
    private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

    public static int Send(string command)
    {
        return mciSendString(command, null, 0, IntPtr.Zero);
    }

    // volume0to100: 0 = silent, 100 = full. Applied to both channels.
    public static void SetVolume(int volume0to100)
    {
        int v = Math.Max(0, Math.Min(100, volume0to100));
        uint level = (uint)(v * 0xFFFF / 100);
        uint dw = (level << 16) | level; // high word = right, low word = left
        waveOutSetVolume(IntPtr.Zero, dw);
    }
}

public class TuioDemo : Form, TuioListener
{
    private struct ModelOrientation
    {
        public float Yaw;
        public float Pitch;
        public float Roll;

        public ModelOrientation(float yaw, float pitch, float roll)
        {
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
        }
    }

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
    private System.Windows.Forms.Timer mainTimer;

    // Users / Favourites
    private List<UserRecord> users = new List<UserRecord>();
    private string loggedInName = "";
    private List<int> userFavourites = new List<int>();
    private string usersJsonPath = "";
    private string favouritesJsonPath = "";

    // 3D
    private Dictionary<int,ObjModel> modelCache = new Dictionary<int,ObjModel>();
    private readonly HashSet<int> modelsBeingLoaded = new HashSet<int>();
    private float artifactAngle = 0f;
    private bool artifactAngleControlledByMarker = false;
    private long activeMarkerSessionId = -1;

    // Static thumbnail cache — one small bitmap per artifact, rendered once at a fixed angle
    // Used for all non-rotating cards (carousel background cards, favourites)
    private Dictionary<int, Bitmap> _thumbnailCache = new Dictionary<int, Bitmap>();
    private const int ThumbSize = 100; // px — small enough to be fast, big enough to look good
    private const float ThumbAngle = 0.6f; // fixed display angle for thumbnails

    // Quantized frame cache for rotating models (artifact view + focused card)
    // Avoids expensive per-frame full rasterization and bitmap allocation.
    private readonly object _frameCacheLock = new object();
    private Dictionary<string, Bitmap> _rotationFrameCache = new Dictionary<string, Bitmap>();
    private Queue<string> _rotationFrameOrder = new Queue<string>();
    private const int MaxRotationFrames = 220;
    private const float RotationFrameStep = 0.06f; // radians (~3.4 degrees)
    private const float DefaultArtifactPitch = -0.35f;
    private const float MarkerPitchAmplitude = 0.65f;
    private const float MarkerRollAmplitude = 0.42f;

    // Rotating card state — only used on Artifact detail screen
    // (Explore page is fully static for performance)

    // Background cache
    private Bitmap bgCache = null;
    private int bgCacheW = -1, bgCacheH = -1;

    // Python socket
    private bool faceDetected = false;

    // Fonts
    private Font fontUI    = new Font("Segoe UI", 11f);
    private Font fontNarration = new Font("Segoe UI", 12.5f, FontStyle.Regular);
    private Font fontTitle = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
    private Font fontSmall = new Font("Segoe UI", 9f);
    private Font fontTab   = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);

    // Tab hit rects
    private Rectangle tabExplore    = Rectangle.Empty;
    private Rectangle tabFavourites = Rectangle.Empty;
    private Rectangle tabArtifact   = Rectangle.Empty;
    private Rectangle favouriteButtonRect = Rectangle.Empty;

    // Status labels
    private Label faceStatusLabel;
    private Label pythonStatusLabel;
    private Panel narrationControlCard;
    private Button narrationPauseButton;
    private Label narrationVolumeLabel;
    private Panel narrationVolumeTrack;
    private Panel narrationVolumeFill;
    private Panel narrationVolumeThumb;
    private bool narrationSliderDragging = false;

    private const string NarrationAlias = "museNarration";
    private bool narrationPaused = false;
    private int narrationVolume = 70; // 0..100
    private string currentNarrationPath = "";

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
        InitNarrationControls();
        this.WindowState = FormWindowState.Maximized;
        UpdateLayout();

        // Single timer drives animation — only the active rotating card + artifact detail spin
        mainTimer = new System.Windows.Forms.Timer();
        mainTimer.Interval = 33;
        mainTimer.Tick += (s,e) => {
            bool isAnimating = false;

            float delta = carouselTarget - carouselAngle;
            if (Math.Abs(delta) > 0.0004f)
            {
                carouselAngle += delta * 0.12f;
                isAnimating = true;
            }
            else
            {
                carouselAngle = carouselTarget;
            }

            if (currentScreen == AppScreen.Artifact)
            {
                if (artifactAngleControlledByMarker)
                {
                    artifactAngle = NormalizeAngle(artifactAngle);
                }
                else
                {
                    artifactAngle = NormalizeAngle(artifactAngle + 0.022f);
                }
                isAnimating = true;
            }

            if (isAnimating) Invalidate();

            int targetInterval = isAnimating ? 33 : 80;
            if (mainTimer.Interval != targetInterval) mainTimer.Interval = targetInterval;
        };
        mainTimer.Start();

        LoadUsers();

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        PrimeInitialModels();

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

        usersJsonPath = "";
        foreach (string p in cands)
            if (File.Exists(p))
            {
                usersJsonPath = p;
                try { users = TinyJson.ParseUsers(File.ReadAllText(p)); } catch { users = new List<UserRecord>(); }
                break;
            }

        if (string.IsNullOrEmpty(usersJsonPath)) usersJsonPath = cands[0];

        ResolveFavouritesPath();
        LoadFavouriteStore();
    }

    private void ResolveFavouritesPath()
    {
        string[] cands = {
            Path.Combine("faces", "favorites.json"),
            Path.Combine(Application.StartupPath, "faces", "favorites.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "faces", "favorites.json"),
            Path.Combine("..", "..", "faces", "favorites.json"),
            Path.Combine("..", "..", "..", "faces", "favorites.json")
        };

        favouritesJsonPath = "";
        foreach (string p in cands)
            if (File.Exists(p))
            {
                favouritesJsonPath = p;
                break;
            }

        if (string.IsNullOrEmpty(favouritesJsonPath)) favouritesJsonPath = cands[0];
    }

    private void LoadFavouriteStore()
    {
        if (string.IsNullOrEmpty(favouritesJsonPath) || !File.Exists(favouritesJsonPath)) return;

        List<UserRecord> storedFavourites;
        try
        {
            storedFavourites = TinyJson.ParseUsers(File.ReadAllText(favouritesJsonPath));
        }
        catch
        {
            return;
        }

        foreach (UserRecord user in users)
            user.Favourites.Clear();

        foreach (UserRecord storedUser in storedFavourites)
        {
            UserRecord targetUser = GetOrCreateUserRecord(storedUser.Name);
            targetUser.Favourites.Clear();
            foreach (string favouriteName in storedUser.Favourites)
                if (!targetUser.Favourites.Exists(delegate(string value) { return value.Equals(favouriteName, StringComparison.OrdinalIgnoreCase); }))
                    targetUser.Favourites.Add(favouriteName);
        }
    }

    private void SetLoggedInUser(string name)
    {
        loggedInName = name;
        userFavourites.Clear();
        UserRecord currentUser = GetOrCreateUserRecord(name);
        foreach (string favouriteName in currentUser.Favourites)
        {
            int favouriteId = GetArtifactIdByName(favouriteName);
            if (favouriteId >= 0 && !userFavourites.Contains(favouriteId)) userFavourites.Add(favouriteId);
        }
        UpdateFaceLabel(true, name);
        Invalidate();
    }

    private void ClearLoggedInUser()
    {
        loggedInName = "";
        userFavourites.Clear();
        favouriteButtonRect = Rectangle.Empty;
        UpdateFaceLabel(false, "");
        if (currentScreen == AppScreen.Favourites)
            GoTo(AppScreen.Explore, -1, false, false);
        else
            Invalidate();
    }

    private UserRecord GetOrCreateUserRecord(string name)
    {
        foreach (UserRecord user in users)
            if (user.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return user;

        UserRecord created = new UserRecord { Name = name };
        users.Add(created);
        return created;
    }

    private int GetArtifactIdByName(string artifactName)
    {
        for (int i = 0; i < ArtifactData.Count; i++)
            if (ArtifactData.Names[i].Equals(artifactName, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private bool IsUserLoggedIn()
    {
        return !string.IsNullOrEmpty(loggedInName);
    }

    private bool SaveFavourites()
    {
        try
        {
            string json = File.Exists(favouritesJsonPath) ? File.ReadAllText(favouritesJsonPath) : "[]";
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object root = serializer.DeserializeObject(json);
            object[] items = root as object[];
            List<Dictionary<string, object>> records = new List<Dictionary<string, object>>();

            if (items != null)
            {
                foreach (object item in items)
                {
                    Dictionary<string, object> record = item as Dictionary<string, object>;
                    if (record != null) records.Add(record);
                }
            }

            Dictionary<string, object> targetRecord = null;
            foreach (Dictionary<string, object> record in records)
            {
                object nameValue;
                if (record.TryGetValue("name", out nameValue) && string.Equals(Convert.ToString(nameValue), loggedInName, StringComparison.OrdinalIgnoreCase))
                {
                    targetRecord = record;
                    break;
                }
            }

            UserRecord currentUser = GetOrCreateUserRecord(loggedInName);
            ArrayList savedFavourites = new ArrayList();
            foreach (string favouriteName in currentUser.Favourites)
                savedFavourites.Add(favouriteName);

            if (targetRecord == null)
            {
                targetRecord = new Dictionary<string, object>();
                targetRecord["name"] = loggedInName;
                records.Add(targetRecord);
            }

            targetRecord["favourites"] = savedFavourites;
            string dir = Path.GetDirectoryName(favouritesJsonPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(favouritesJsonPath, serializer.Serialize(records));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ShowLoginRequiredPopup()
    {
        MessageBox.Show(this,
            "Please log in with face detection first, then you can add this artifact to your favourites.",
            "Login Required",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void AddCurrentArtifactToFavourites()
    {
        if (artifactID < 0 || artifactID >= ArtifactData.Count) return;

        if (!IsUserLoggedIn())
        {
            ShowLoginRequiredPopup();
            return;
        }

        string artifactName = ArtifactData.Names[artifactID];
        UserRecord currentUser = GetOrCreateUserRecord(loggedInName);

        if (userFavourites.Contains(artifactID) || currentUser.Favourites.Exists(delegate(string favourite) { return favourite.Equals(artifactName, StringComparison.OrdinalIgnoreCase); }))
        {
            MessageBox.Show(this,
                "This artifact is already in your favourites.",
                "Already Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        userFavourites.Add(artifactID);
        currentUser.Favourites.Add(artifactName);

        if (!SaveFavourites())
        {
            currentUser.Favourites.RemoveAll(delegate(string favourite) { return favourite.Equals(artifactName, StringComparison.OrdinalIgnoreCase); });
            userFavourites.Remove(artifactID);
            MessageBox.Show(this,
            "The favourite could not be saved to favorites.json.",
                "Save Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Invalidate();
    }

    private void GoTo(AppScreen screen, int id)
    {
        GoTo(screen, id, true, true);
    }

    private void GoTo(AppScreen screen, int id, bool autoPlayNarration)
    {
        GoTo(screen, id, autoPlayNarration, true);
    }

    private void GoTo(AppScreen screen, int id, bool autoPlayNarration, bool resetArtifactAngle)
    {
        if (screen == AppScreen.Favourites && !IsUserLoggedIn())
        {
            currentScreen = AppScreen.Explore;
            Invalidate();
            return;
        }

        bool leavingArtifact = (currentScreen == AppScreen.Artifact && screen != AppScreen.Artifact);
        if (leavingArtifact) StopNarrationAudio();

        int oldArtifact = artifactID;
        int targetArtifact = (id >= 0) ? id : artifactID;
        bool sameArtifact = (screen == AppScreen.Artifact && currentScreen == AppScreen.Artifact && targetArtifact >= 0 && targetArtifact == oldArtifact);

        currentScreen = screen;
        if (id >= 0) artifactID = id;
        if (screen == AppScreen.Artifact && artifactID >= 0)
        {
            if (resetArtifactAngle) artifactAngle = 0f;
            // Play narration only for explicit artifact activation (click on artifact card or marker scan).
            if (autoPlayNarration && !sameArtifact) PlayNarrationAudio(artifactID);
        }
        UpdateNarrationControlsVisibility();
        Invalidate();
    }

    private void GoTo(AppScreen screen) { GoTo(screen, -1, false); }

    private void Form_MouseClick(object sender, MouseEventArgs e)
    {
        Point p = e.Location;
        if (tabExplore.Contains(p))    { GoTo(AppScreen.Explore);    return; }
        if (tabFavourites.Contains(p)) { GoTo(AppScreen.Favourites); return; }
        if (tabArtifact.Contains(p) && artifactID >= 0) { GoTo(AppScreen.Artifact); return; }
        if (currentScreen == AppScreen.Artifact && favouriteButtonRect.Contains(p)) { AddCurrentArtifactToFavourites(); return; }

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
        TryGetModelBg(idx);
    }

    private void PrimeInitialModels()
    {
        if (ArtifactData.Count <= 0) return;
        TryGetModelBg(carouselFocused);
        if (ArtifactData.Count > 1) TryGetModelBg((carouselFocused + 1) % ArtifactData.Count);
        if (ArtifactData.Count > 2) TryGetModelBg((carouselFocused - 1 + ArtifactData.Count) % ArtifactData.Count);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode   = SmoothingMode.HighSpeed;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.Bilinear;
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
        if (bgCache == null || bgCacheW != width || bgCacheH != height)
        {
            if (bgCache != null) bgCache.Dispose();
            bgCache = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics bg = Graphics.FromImage(bgCache))
            using (var br = new LinearGradientBrush(new Rectangle(0,0,width,height),
                Color.FromArgb(10,22,48), Color.FromArgb(2,8,22), LinearGradientMode.Vertical))
                bg.FillRectangle(br, 0, 0, width, height);
            bgCacheW = width; bgCacheH = height;
        }
        g.DrawImageUnscaled(bgCache, 0, 0);
    }

    private void DrawHeader(Graphics g)
    {
        using (var br = new SolidBrush(Color.FromArgb(180,5,15,35)))
            g.FillRectangle(br, 0, 0, width, 60);
        using (var pen = new Pen(Color.FromArgb(80,100,160,220), 1))
            g.DrawLine(pen, 0, 60, width, 60);
        using (var br = new SolidBrush(Color.FromArgb(200,180,220,255)))
            g.DrawString("MuseSense", fontTitle, br, 18, 12);

        string[] labels = IsUserLoggedIn()
            ? new string[] { "Explore", "Favourites", "Artifact" }
            : new string[] { "Explore", "Artifact" };
        AppScreen[] screens = IsUserLoggedIn()
            ? new AppScreen[] { AppScreen.Explore, AppScreen.Favourites, AppScreen.Artifact }
            : new AppScreen[] { AppScreen.Explore, AppScreen.Artifact };
        int tabW=130, tabH=36, tabY=12;
        int startX = width/2 - (labels.Length*tabW)/2;
        tabExplore = Rectangle.Empty;
        tabFavourites = Rectangle.Empty;
        tabArtifact = Rectangle.Empty;

        for (int i = 0; i < labels.Length; i++)
        {
            Rectangle r = new Rectangle(startX + i*tabW, tabY, tabW-4, tabH);
            if (screens[i] == AppScreen.Explore) tabExplore = r;
            else if (screens[i] == AppScreen.Favourites) tabFavourites = r;
            else tabArtifact = r;
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

    private void DrawExplore(Graphics g, int top)
    {
        using (var br = new SolidBrush(Color.FromArgb(200,180,210,255)))
            g.DrawString("Explore Artifacts", fontTitle, br, 20, top+10);
        using (var br = new SolidBrush(Color.FromArgb(140,180,200,220)))
            g.DrawString("Click a card to select  |  Click again to open  |  Place TUIO marker to jump directly",
                fontSmall, br, 20, top+46);

        int cy = top + 80 + (height - top - 80) / 2;
        float step = (float)(2 * Math.PI / ArtifactData.Count);
        for (int i = 0; i < ArtifactData.Count; i++)
            if (i != carouselFocused) DrawCarouselCard(g, i, carouselAngle + i*step, cy);
        if (carouselFocused >= 0 && carouselFocused < ArtifactData.Count)
            DrawCarouselCard(g, carouselFocused, carouselAngle + carouselFocused*step, cy);

        using (var br = new SolidBrush(Color.FromArgb(100,160,180,200)))
            g.DrawString("Arrow keys or click to rotate", fontSmall, br, width/2-100, height-28);
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
            // Explore page: always static thumbnail — no animation, no rasterization per frame
            DrawThumbnail(g, idx, model, cx, cardTop + cardH/3, ms);
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
            DrawThumbnail(g, id, model, r.Left+r.Width/2, r.Top+r.Height/3, ms);
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
    private void DrawArtifact(Graphics g, int top)
    {
        if (artifactID < 0 || artifactID >= ArtifactData.Count)
        {
            favouriteButtonRect = Rectangle.Empty;
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
            RenderObjModelDirect(g, model, artifactID, mx, my, GetArtifactOrientation(), modelSize, ArtifactData.Colors[artifactID]);
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

        int favouriteButtonWidth = Math.Min(190, rw);
        favouriteButtonRect = new Rectangle(rx, ry + 214, favouriteButtonWidth, 42);
        bool isFav = userFavourites.Contains(artifactID);
        Color favouriteFill = isFav ? Color.FromArgb(210, 176, 131, 36) : Color.FromArgb(185, 36, 72, 126);
        Color favouriteBorder = isFav ? Color.FromArgb(255, 255, 211, 96) : Color.FromArgb(220, 120, 180, 255);
        using (var path = RoundRect(favouriteButtonRect, 10))
        using (var brush = new SolidBrush(favouriteFill))
        using (var pen = new Pen(favouriteBorder, 1.8f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        using (var br = new SolidBrush(Color.White))
        {
            string favouriteText = isFav ? "Favourited" : "Favourite";
            SizeF textSize = g.MeasureString(favouriteText, fontTab);
            g.DrawString(favouriteText, fontTab, br,
                favouriteButtonRect.Left + (favouriteButtonRect.Width - textSize.Width) / 2,
                favouriteButtonRect.Top + (favouriteButtonRect.Height - textSize.Height) / 2);
        }

        using (var br = new SolidBrush(Color.FromArgb(220,170,210,255)))
            g.DrawString("Narration", fontTab, br, rx, ry+265);
        using (var br = new SolidBrush(Color.FromArgb(210,210,225,245)))
            g.DrawString(ArtifactData.Narrations[artifactID], fontNarration, br, new RectangleF(rx, ry+300, rw, 250));
        using (var br = new SolidBrush(Color.FromArgb(120,140,200,140)))
            g.DrawString(artifactAngleControlledByMarker
                ? "Marker detected: rotate the TUIO marker to tilt and spin the model in 3D"
                : "Marker not detected: the model is rotating automatically",
                fontSmall, br, rx, ry+560);

        DrawArtifactQuickInfo(g, top);

        using (var br = new SolidBrush(isFav ? Color.FromArgb(220,255,200,50) : Color.FromArgb(80,180,180,180)))
            g.DrawString(isFav ? "* In your Favourites" : "Not in Favourites", fontSmall, br, rx, ry+590);
        using (var br = new SolidBrush(Color.FromArgb(100,100,160,200)))
            g.DrawString("Marker ID: " + artifactID, fontSmall, br, rx, ry+615);
    }

    private void PlayNarrationAudio(int id)
    {
        string rel = ArtifactData.GetAudioPath(id);
        if (string.IsNullOrEmpty(rel)) return;
        string full = ResolveAsset(rel);
        if (string.IsNullOrEmpty(full)) return;

        // Always stop current narration before starting another to prevent overlap.
        StopNarrationAudioInternal();

        try
        {
            string escaped = full.Replace("\"", "\\\"");
            if (MciAudio.Send("open \"" + escaped + "\" type waveaudio alias " + NarrationAlias) != 0) return;
            ApplyNarrationVolume();
            if (MciAudio.Send("play " + NarrationAlias + " from 0") != 0)
            {
                MciAudio.Send("close " + NarrationAlias);
                return;
            }
            narrationPaused = false;
            currentNarrationPath = full;
            if (narrationPauseButton != null) narrationPauseButton.Text = "Pause Narration";
        }
        catch { }
    }

    private void StopNarrationAudio()
    {
        StopNarrationAudioInternal();
    }

    private void StopNarrationAudioInternal()
    {
        MciAudio.Send("stop " + NarrationAlias);
        MciAudio.Send("close " + NarrationAlias);
        narrationPaused = false;
        currentNarrationPath = "";
        if (narrationPauseButton != null) narrationPauseButton.Text = "Pause Narration";
    }

    private void TogglePauseNarration()
    {
        if (string.IsNullOrEmpty(currentNarrationPath)) return;
        if (narrationPaused)
        {
            int rc = MciAudio.Send("resume " + NarrationAlias);
            if (rc != 0) rc = MciAudio.Send("play " + NarrationAlias);
            if (rc == 0)
            {
                narrationPaused = false;
                narrationPauseButton.Text = "Pause Narration";
            }
        }
        else
        {
            if (MciAudio.Send("pause " + NarrationAlias) == 0)
            {
                narrationPaused = true;
                narrationPauseButton.Text = "Resume Narration";
            }
        }
    }

    private void SetNarrationVolume(int volume0to100)
    {
        int v = Math.Max(0, Math.Min(100, volume0to100));
        narrationVolume = v;
        if (narrationVolumeLabel != null) narrationVolumeLabel.Text = "Volume " + v + "%";
        UpdateNarrationSliderVisual();
        ApplyNarrationVolume();
    }

    private void ApplyNarrationVolume()
    {
        MciAudio.SetVolume(narrationVolume);
    }

    private void UpdateNarrationSliderVisual()
    {
        if (narrationVolumeTrack == null || narrationVolumeFill == null || narrationVolumeThumb == null) return;
        int trackW = narrationVolumeTrack.Width;
        if (trackW <= 0) return;
        int fillW = (int)((trackW * narrationVolume) / 100.0);
        fillW = Math.Max(6, Math.Min(trackW, fillW));
        narrationVolumeFill.Width = fillW;
        narrationVolumeThumb.Left = Math.Max(0, Math.Min(trackW - narrationVolumeThumb.Width, fillW - narrationVolumeThumb.Width / 2));
    }

    private void SetNarrationVolumeFromTrackX(int x)
    {
        int trackW = narrationVolumeTrack.Width;
        if (trackW <= 0) return;
        int clamped = Math.Max(0, Math.Min(trackW, x));
        int v = (int)Math.Round((clamped * 100.0) / trackW);
        SetNarrationVolume(v);
    }

    private void BeginNarrationSliderDrag(int trackX)
    {
        narrationSliderDragging = true;
        if (narrationVolumeTrack != null) narrationVolumeTrack.Capture = true;
        SetNarrationVolumeFromTrackX(trackX);
    }

    private void ContinueNarrationSliderDrag(int trackX)
    {
        if (!narrationSliderDragging) return;
        SetNarrationVolumeFromTrackX(trackX);
    }

    private void EndNarrationSliderDrag()
    {
        narrationSliderDragging = false;
        if (narrationVolumeTrack != null) narrationVolumeTrack.Capture = false;
    }

    private void DrawArtifactQuickInfo(Graphics g, int top)
    {
        if (artifactID < 0 || artifactID >= ArtifactData.Count) return;
        Rectangle box = new Rectangle(18, top + 14, 300, 150);
        using (var path = RoundRect(box, 12))
        using (var br = new SolidBrush(Color.FromArgb(170, 8, 18, 40))) g.FillPath(br, path);
        using (var path = RoundRect(box, 12))
        using (var pen = new Pen(Color.FromArgb(130, 120, 170, 220), 1.2f)) g.DrawPath(pen, path);

        int x = box.Left + 12;
        int y = box.Top + 10;
        using (var br = new SolidBrush(Color.FromArgb(230, 235, 220, 160)))
            g.DrawString("Artifact Info", fontSmall, br, x, y);
        y += 22;
        using (var br = new SolidBrush(Color.FromArgb(235, 245, 245, 250)))
            g.DrawString("Name: " + ArtifactData.Names[artifactID], fontSmall, br, x, y);
        y += 24;
        using (var br = new SolidBrush(Color.FromArgb(215, 215, 230, 245)))
            g.DrawString("Birth Date: " + ArtifactData.BirthDates[artifactID], fontSmall, br, x, y);
        y += 22;
        using (var br = new SolidBrush(Color.FromArgb(215, 215, 230, 245)))
            g.DrawString("Era: " + ArtifactData.Eras[artifactID], fontSmall, br, x, y);
        y += 22;
        using (var br = new SolidBrush(Color.FromArgb(215, 215, 230, 245)))
            g.DrawString("Origin: " + ArtifactData.Origins[artifactID], fontSmall, br, x, y);
    }

    // Called only from UI thread. Returns cached model or null while loading in background.
    private ObjModel TryGetModel(int id)
    {
        if (modelCache.ContainsKey(id)) return modelCache[id];
        TryGetModelBg(id);
        return null;
    }
    // Thread-safe: queues a background load if not already in progress.
    private void TryGetModelBg(int id)
    {
        lock (modelsBeingLoaded)
        {
            if (modelCache.ContainsKey(id)) return;
            if (modelsBeingLoaded.Contains(id)) return;
            modelsBeingLoaded.Add(id);
        }
        int capturedId = id;
        ThreadPool.QueueUserWorkItem(_ => {
            string objPath = ArtifactData.GetObjPath(capturedId);
            if (string.IsNullOrEmpty(objPath))
            {
                lock (modelsBeingLoaded) modelsBeingLoaded.Remove(capturedId);
                return;
            }
            ObjModel m = LoadObjModel(objPath);
            if (IsHandleCreated)
                BeginInvoke(new MethodInvoker(() => {
                    modelCache[capturedId] = m;
                    lock (modelsBeingLoaded) modelsBeingLoaded.Remove(capturedId);
                    // Build static thumbnail for this artifact on a background thread
                    if (m != null)
                    {
                        int tid = capturedId; ObjModel tm = m;
                        ThreadPool.QueueUserWorkItem(__ => BuildThumbnail(tid, tm));
                    }
                    Invalidate();
                }));
            else
            {
                modelCache[capturedId] = m;
                lock (modelsBeingLoaded) modelsBeingLoaded.Remove(capturedId);
            }
        });
    }

    // Renders a single static bitmap for this artifact at ThumbAngle — called once per artifact
    private readonly object _thumbLock = new object();
    private void BuildThumbnail(int id, ObjModel model)
    {
        Bitmap bmp = RasterizeSingle(model, ThumbSize, ThumbSize, new ModelOrientation(ThumbAngle, DefaultArtifactPitch, 0f), ArtifactData.Colors[id], true);
        lock (_thumbLock)
        {
            if (_thumbnailCache.ContainsKey(id)) { bmp.Dispose(); return; }
            _thumbnailCache[id] = bmp;
        }
        if (IsHandleCreated) BeginInvoke(new MethodInvoker(Invalidate));
    }

    // Draws the static thumbnail for a card, scaled to fit the requested display size
    private void DrawThumbnail(Graphics g, int id, ObjModel model, int cx, int cy, int displaySize)
    {
        Bitmap thumb;
        lock (_thumbLock) _thumbnailCache.TryGetValue(id, out thumb);
        if (thumb != null)
        {
            g.DrawImage(thumb, cx - displaySize/2, cy - displaySize/2, displaySize, displaySize);
        }
        // else: thumbnail still building — draw nothing (card background is already visible)
    }

    // Renders the rotating model directly to the Graphics surface (used only for the one active card)
    private void RenderObjModelDirect(Graphics g, ObjModel model, int cacheId, int cx, int cy, ModelOrientation orientation, int size, Color baseColor)
    {
        if (model == null) return;
        int W = Math.Max(60, size);
        int internalW = Math.Min(W, 420);
        bool useTextures = model.MatTextures.Count > 0;
        int qYaw = (int)Math.Round(orientation.Yaw / RotationFrameStep);
        int qPitch = (int)Math.Round(orientation.Pitch / RotationFrameStep);
        int qRoll = (int)Math.Round(orientation.Roll / RotationFrameStep);
        string key = cacheId + "|" + internalW + "|" + qYaw + "|" + qPitch + "|" + qRoll + "|" + baseColor.ToArgb() + "|" + (useTextures ? 1 : 0);

        Bitmap bmp;
        lock (_frameCacheLock)
        {
            if (!_rotationFrameCache.TryGetValue(key, out bmp))
            {
                bmp = RasterizeSingle(
                    model,
                    internalW,
                    internalW,
                    new ModelOrientation(qYaw * RotationFrameStep, qPitch * RotationFrameStep, qRoll * RotationFrameStep),
                    baseColor,
                    useTextures);
                _rotationFrameCache[key] = bmp;
                _rotationFrameOrder.Enqueue(key);

                while (_rotationFrameOrder.Count > MaxRotationFrames)
                {
                    string oldKey = _rotationFrameOrder.Dequeue();
                    Bitmap oldBmp;
                    if (_rotationFrameCache.TryGetValue(oldKey, out oldBmp))
                    {
                        _rotationFrameCache.Remove(oldKey);
                        oldBmp.Dispose();
                    }
                }
            }
        }

        g.DrawImage(bmp, cx - size/2, cy - size/2, size, size);
    }

    private void ClearRotationFrameCache()
    {
        lock (_frameCacheLock)
        {
            foreach (Bitmap bmp in _rotationFrameCache.Values) bmp.Dispose();
            _rotationFrameCache.Clear();
            _rotationFrameOrder.Clear();
        }
    }

    private void ClearThumbnailCache()
    {
        lock (_thumbLock)
        {
            foreach (Bitmap bmp in _thumbnailCache.Values) bmp.Dispose();
            _thumbnailCache.Clear();
        }
    }

    // Core rasterizer — allocates its own buffers so it is fully thread-safe
    private Bitmap RasterizeSingle(ObjModel model, int W, int H, ModelOrientation orientation, Color baseColor, bool useTextures)
    {
        int[] pixels = new int[W * H];
        float[] zBuf = new float[W * H];
        for (int i = 0; i < zBuf.Length; i++) zBuf[i] = float.NegativeInfinity;

        float scale = (W * 0.95f) / model.Radius;
        float yaw = orientation.Yaw, pitch = orientation.Pitch, roll = orientation.Roll, camZ = W * 3.2f, proj = W * 1.7f;
        float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
        float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
        float cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
        int vc = model.Verts.Count;
        float[] transX = new float[vc];
        float[] transY = new float[vc];
        float[] transZ = new float[vc];
        PointF[] proj2 = new PointF[vc];
        float[] depth = new float[vc];

        for (int i = 0; i < vc; i++)
        {
            Vec3 v = model.Verts[i];
            float x1 = v.X * cy + v.Z * sy;
            float z1 = -v.X * sy + v.Z * cy;
            float y1 = v.Y;
            float y2 = y1 * cp - z1 * sp;
            float z2 = y1 * sp + z1 * cp;
            float x3 = x1 * cr - y2 * sr;
            float y3 = x1 * sr + y2 * cr;
            float tx = x3 * scale;
            float ty = y3 * scale;
            float tz = z2 * scale;

            transX[i] = tx;
            transY[i] = ty;
            transZ[i] = tz;

            float z = tz + camZ; if (z < 1f) z = 1f;
            depth[i] = tz;
            proj2[i] = new PointF(W * 0.5f + tx * proj / z, H * 0.5f - ty * proj / z);
        }

        const float ldx = 0.1760902f;
        const float ldy = -0.4402255f;
        const float ldz = 0.8804509f;
        int faceCount = model.Faces.Count;
        for (int i = 0; i < faceCount; i++)
        {
            Face3 f = model.Faces[i];
            int ia = f.A, ib = f.B, ic = f.C;
            float ax = transX[ia], ay = transY[ia], az = transZ[ia];
            float bx = transX[ib], by = transY[ib], bz = transZ[ib];
            float cx = transX[ic], cy2 = transY[ic], cz = transZ[ic];

            float nx = (by - ay) * (cz - az) - (bz - az) * (cy2 - ay);
            float ny = (bz - az) * (cx - ax) - (bx - ax) * (cz - az);
            float nz = (bx - ax) * (cy2 - ay) - (by - ay) * (cx - ax);
            float nl = (float)Math.Sqrt(nx*nx+ny*ny+nz*nz);
            if (nl < 1e-5f) continue;
            nx /= nl; ny /= nl; nz /= nl;
            if (nz < 0f) continue; // back-face cull — skip ~50% of triangles
            float lf = Math.Min(1.18f, 0.68f + 0.42f * Math.Abs(nx * ldx + ny * ldy + nz * ldz));

            Color fc = baseColor;
            TextureData tex = null;
            if (!string.IsNullOrEmpty(f.Mat))
            {
                Color materialColor;
                if (model.MatColors.TryGetValue(f.Mat, out materialColor)) fc = materialColor;
                if (useTextures) model.MatTextures.TryGetValue(f.Mat, out tex);
            }

            Vec2 uv1 = (f.TA>=0&&f.TA<model.UVs.Count)?model.UVs[f.TA]:null;
            Vec2 uv2 = (f.TB>=0&&f.TB<model.UVs.Count)?model.UVs[f.TB]:null;
            Vec2 uv3 = (f.TC>=0&&f.TC<model.UVs.Count)?model.UVs[f.TC]:null;
            RasterTri(pixels, zBuf, W, H, proj2[ia], proj2[ib], proj2[ic], depth[ia], depth[ib], depth[ic], uv1, uv2, uv3, tex, fc, lf);
        }

        Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        BitmapData bd = bmp.LockBits(new Rectangle(0,0,W,H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    private Vec3 RotV(Vec3 v,float yaw,float pitch,float roll)
    {
        float cy=(float)Math.Cos(yaw),sy=(float)Math.Sin(yaw),cp=(float)Math.Cos(pitch),sp=(float)Math.Sin(pitch),cr=(float)Math.Cos(roll),sr=(float)Math.Sin(roll);
        float x1=v.X*cy+v.Z*sy,z1=-v.X*sy+v.Z*cy,y1=v.Y;
        float y2=y1*cp-z1*sp,z2=y1*sp+z1*cp;
        return new Vec3(x1*cr-y2*sr,x1*sr+y2*cr,z2);
    }
    private Vec3 Norm(Vec3 v){float l=(float)Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z);return l<1e-5f?v:new Vec3(v.X/l,v.Y/l,v.Z/l);}

    private void RasterTri(int[] px,float[] zb,int W,int H,PointF p1,PointF p2,PointF p3,float z1,float z2,float z3,Vec2 uv1,Vec2 uv2,Vec2 uv3,TextureData tex,Color fc,float lf)
    {
        int x0=(int)Math.Max(0,Math.Floor(Math.Min(p1.X,Math.Min(p2.X,p3.X))));
        int x1=(int)Math.Min(W-1,Math.Ceiling(Math.Max(p1.X,Math.Max(p2.X,p3.X))));
        int y0=(int)Math.Max(0,Math.Floor(Math.Min(p1.Y,Math.Min(p2.Y,p3.Y))));
        int y1=(int)Math.Min(H-1,Math.Ceiling(Math.Max(p1.Y,Math.Max(p2.Y,p3.Y))));
        float denom=((p2.Y-p3.Y)*(p1.X-p3.X)+(p3.X-p2.X)*(p1.Y-p3.Y));
        if(Math.Abs(denom)<1e-5f)return;
        bool useTex=tex!=null&&uv1!=null&&uv2!=null&&uv3!=null;
        float exp = 1.12f;
        int staticLit = Color.FromArgb(
            255,
            Math.Max(0,Math.Min(255,(int)(fc.R*lf*exp+8))),
            Math.Max(0,Math.Min(255,(int)(fc.G*lf*exp+8))),
            Math.Max(0,Math.Min(255,(int)(fc.B*lf*exp+8)))
        ).ToArgb();
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
        {
            float w1=((p2.Y-p3.Y)*(x-p3.X)+(p3.X-p2.X)*(y-p3.Y))/denom;
            float w2=((p3.Y-p1.Y)*(x-p3.X)+(p1.X-p3.X)*(y-p3.Y))/denom;
            float w3=1f-w1-w2;
            if(w1<0||w2<0||w3<0)continue;
            float z=w1*z1+w2*z2+w3*z3; int idx=y*W+x;
            if(z<=zb[idx])continue; zb[idx]=z;
            if(!useTex){px[idx]=staticLit;continue;}
            Color sc;
            {float u=w1*uv1.U+w2*uv2.U+w3*uv3.U,v=w1*uv1.V+w2*uv2.V+w3*uv3.V;
                u=Math.Max(0f,Math.Min(1f,u));v=Math.Max(0f,Math.Min(1f,v));
                int tx = Math.Max(0,Math.Min(tex.Width-1,(int)(u*(tex.Width-1))));
                int ty = Math.Max(0,Math.Min(tex.Height-1,(int)((1f-v)*(tex.Height-1))));
                sc=Color.FromArgb(tex.Pixels[ty*tex.Width+tx]);}
            px[idx]=Color.FromArgb(255,Math.Max(0,Math.Min(255,(int)(sc.R*lf*exp+8))),Math.Max(0,Math.Min(255,(int)(sc.G*lf*exp+8))),Math.Max(0,Math.Min(255,(int)(sc.B*lf*exp+8)))).ToArgb();
        }
    }
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
        foreach(var kv in mats)
        {
            Color fc=kv.Value.Diffuse;
            if (kv.Value.TexBmp != null) fc = AvgColor(kv.Value.TexBmp, fc);
            m.MatColors[kv.Key]=fc;
            if(kv.Value.TexBmp!=null)
            {
                m.MatTextures[kv.Key]=ToTextureData(kv.Value.TexBmp);
                kv.Value.TexBmp.Dispose();
                kv.Value.TexBmp = null;
            }
        }
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
                if(line.StartsWith("map_Kd "))
                {
                    string tp=line.Substring(7).Trim();
                    if(!string.IsNullOrEmpty(tp))
                    {
                        string[] parts=tp.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                        string texRel=parts.Length>0?parts[parts.Length-1]:tp;
                        if(!string.IsNullOrEmpty(texRel))res[cur].TexPath=ResolveTexturePath(mdir,texRel);
                    }
                }
            }
            foreach(var kv in res)if(!string.IsNullOrEmpty(kv.Value.TexPath)&&File.Exists(kv.Value.TexPath))try{kv.Value.TexBmp=new Bitmap(kv.Value.TexPath);}catch{}
        }
        return res;
    }

    private string ResolveTexturePath(string materialDir, string textureRef)
    {
        if (string.IsNullOrEmpty(textureRef)) return null;

        string cleaned = textureRef.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string[] candidates = {
            cleaned,
            Path.Combine(materialDir, cleaned),
            Path.Combine(materialDir, Path.GetFileName(cleaned)),
            Path.Combine(materialDir, "textures", Path.GetFileName(cleaned)),
            Path.Combine(Directory.GetParent(materialDir) != null ? Directory.GetParent(materialDir).FullName : materialDir, Path.GetFileName(cleaned)),
            Path.Combine(Directory.GetParent(materialDir) != null ? Directory.GetParent(materialDir).FullName : materialDir, "textures", Path.GetFileName(cleaned))
        };

        foreach (string candidate in candidates)
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return candidate;

        return Path.Combine(materialDir, cleaned);
    }

    private bool TryF(string s,out float v){return float.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v);}
    private TextureData ToTextureData(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int[] pix = new int[w * h];
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(bd.Scan0, pix, 0, pix.Length); }
        finally { bmp.UnlockBits(bd); }
        return new TextureData { Width = w, Height = h, Pixels = pix };
    }
    private Color AvgColor(Bitmap bmp, Color fallback)
    {
        try
        {
            TextureData t = ToTextureData(bmp);
            int sx = Math.Max(1, t.Width / 60), sy = Math.Max(1, t.Height / 60);
            long sr = 0, sg = 0, sb = 0, cnt = 0;
            for (int y = 0; y < t.Height; y += sy)
            for (int x = 0; x < t.Width; x += sx)
            {
                Color c = Color.FromArgb(t.Pixels[y * t.Width + x]);
                sr += c.R; sg += c.G; sb += c.B; cnt++;
            }
            if (cnt == 0) return fallback;
            return Color.FromArgb(220, (int)(sr / cnt), (int)(sg / cnt), (int)(sb / cnt));
        }
        catch { return fallback; }
    }
    private int ObjIdx(string tok,int cnt){int i;if(!int.TryParse(tok.Split('/')[0],out i))return -1;return i>0?i-1:i<0?cnt+i:-1;}
    private int ObjTexIdx(string tok,int cnt){string[] c=tok.Split('/');if(c.Length<2||string.IsNullOrEmpty(c[1]))return -1;int i;if(!int.TryParse(c[1],out i))return -1;return i>0?i-1:i<0?cnt+i:-1;}
    private string Resolve(string rel){if(string.IsNullOrEmpty(rel))return null;string[] cands={rel,Path.Combine(Application.StartupPath,rel),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,rel)};foreach(string c in cands)if(File.Exists(c))return c;return null;}
    private string ResolveAsset(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        string[] cands = {
            rel,
            Path.Combine(Application.StartupPath, rel),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel),
            Path.Combine("..", "..", rel),
            Path.Combine("..", "..", "..", rel),
            Path.Combine("..", "..", "..", "..", rel)
        };
        foreach (string c in cands) if (File.Exists(c)) return c;
        return null;
    }

    private static float NormalizeAngle(float angle)
    {
        float fullTurn = (float)(Math.PI * 2.0);
        while (angle >= fullTurn) angle -= fullTurn;
        while (angle < 0f) angle += fullTurn;
        return angle;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private ModelOrientation GetArtifactOrientation()
    {
        float yaw = NormalizeAngle(artifactAngle);
        if (!artifactAngleControlledByMarker)
            return new ModelOrientation(yaw, DefaultArtifactPitch, 0f);

        float pitch = DefaultArtifactPitch + (float)Math.Sin(yaw) * MarkerPitchAmplitude;
        float roll = (float)Math.Cos(yaw * 0.85f) * MarkerRollAmplitude;
        return new ModelOrientation(yaw, Clamp(pitch, -1.15f, 1.15f), roll);
    }

    private void ApplyMarkerControl(TuioObject o, bool navigateToArtifact)
    {
        int idx = ArtifactData.GetIndexByTuioId(o.SymbolID);
        if (idx < 0 || !IsHandleCreated) return;

        BeginInvoke(new MethodInvoker(() =>
        {
            artifactAngleControlledByMarker = true;
            activeMarkerSessionId = o.SessionID;
            artifactAngle = NormalizeAngle((float)o.Angle);

            if (navigateToArtifact)
                GoTo(AppScreen.Artifact, idx, true, false);
            else if (currentScreen == AppScreen.Artifact && artifactID == idx)
                Invalidate();
        }));
    }

    private void ReleaseMarkerControl(long sessionId)
    {
        if (activeMarkerSessionId != sessionId || !IsHandleCreated) return;

        BeginInvoke(new MethodInvoker(() =>
        {
            if (activeMarkerSessionId != sessionId) return;

            artifactAngleControlledByMarker = false;
            activeMarkerSessionId = -1;
            Invalidate();
        }));
    }

    public void addTuioObject(TuioObject o)
    {
        lock(objectList) objectList[o.SessionID]=o;
        if(verbose) Console.WriteLine("add obj "+o.SymbolID);
        ApplyMarkerControl(o, true);
    }
    public void updateTuioObject(TuioObject o)
    {
        lock(objectList) if(objectList.ContainsKey(o.SessionID)) objectList[o.SessionID]=o;
        if (activeMarkerSessionId == o.SessionID || ArtifactData.GetIndexByTuioId(o.SymbolID) >= 0)
            ApplyMarkerControl(o, false);
    }
    public void removeTuioObject(TuioObject o)
    {
        lock(objectList) objectList.Remove(o.SessionID);
        ReleaseMarkerControl(o.SessionID);
    }
    public void addTuioCursor(TuioCursor c){lock(cursorList)cursorList[c.SessionID]=c;}
    public void updateTuioCursor(TuioCursor c){}
    public void removeTuioCursor(TuioCursor c){lock(cursorList)cursorList.Remove(c.SessionID);}
    public void addTuioBlob(TuioBlob b){lock(blobList)blobList[b.SessionID]=b;}
    public void updateTuioBlob(TuioBlob b){}
    public void removeTuioBlob(TuioBlob b){lock(blobList)blobList.Remove(b.SessionID);}
    // mainTimer already drives Invalidate at 30fps; TUIO refresh is a no-op to avoid redundant repaints
    public void refresh(TuioTime t) { }
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
        else if(msg.Contains("face:lost")||msg.Contains("face lost"))
        {
            faceDetected=false;
            this.BeginInvoke(new MethodInvoker(ClearLoggedInUser));
        }
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

    private void InitNarrationControls()
    {
        narrationControlCard = new ThemedPanel
        {
            Size = new Size(236, 126),
            Visible = false
        };
        this.Controls.Add(narrationControlCard);

        narrationPauseButton = new Button
        {
            Text = "Pause Narration",
            Size = new Size(206, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 92, 166),
            ForeColor = Color.FromArgb(235, 245, 255),
            Visible = false,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };
        narrationPauseButton.FlatAppearance.BorderColor = Color.FromArgb(140, 190, 235);
        narrationPauseButton.FlatAppearance.BorderSize = 1;
        narrationPauseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 118, 198);
        narrationPauseButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 72, 132);
        narrationPauseButton.Click += (s, e) => TogglePauseNarration();
        narrationControlCard.Controls.Add(narrationPauseButton);

        narrationVolumeLabel = new Label
        {
            Text = "Volume 70%",
            AutoSize = false,
            Size = new Size(160, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(220, 235, 255),
            BackColor = Color.FromArgb(0, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Visible = false
        };
        narrationControlCard.Controls.Add(narrationVolumeLabel);

        narrationVolumeTrack = new Panel
        {
            Size = new Size(206, 18),
            BackColor = Color.FromArgb(24, 44, 75),
            Visible = false
        };
        narrationVolumeTrack.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, narrationVolumeTrack.Width - 1, narrationVolumeTrack.Height - 1);
            using (GraphicsPath gp = new GraphicsPath())
            {
                int rad = 8; int d = rad * 2;
                gp.AddArc(r.Left, r.Top, d, d, 180, 90);
                gp.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                gp.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                gp.CloseFigure();
                using (SolidBrush br = new SolidBrush(Color.FromArgb(30, 52, 86))) e.Graphics.FillPath(br, gp);
                using (Pen p = new Pen(Color.FromArgb(90, 150, 210), 1f)) e.Graphics.DrawPath(p, gp);
            }
        };
        narrationVolumeTrack.MouseDown += (s, e) => BeginNarrationSliderDrag(e.X);
        narrationVolumeTrack.MouseMove += (s, e) => ContinueNarrationSliderDrag(e.X);
        narrationVolumeTrack.MouseUp += (s, e) => EndNarrationSliderDrag();
        narrationControlCard.Controls.Add(narrationVolumeTrack);

        narrationVolumeFill = new Panel
        {
            Height = 18,
            Width = 1,
            BackColor = Color.FromArgb(72, 170, 255),
            Enabled = false
        };
        narrationVolumeTrack.Controls.Add(narrationVolumeFill);
        narrationVolumeFill.MouseDown += (s, e) => BeginNarrationSliderDrag(narrationVolumeFill.Left + e.X);
        narrationVolumeFill.MouseMove += (s, e) => ContinueNarrationSliderDrag(narrationVolumeFill.Left + e.X);
        narrationVolumeFill.MouseUp += (s, e) => EndNarrationSliderDrag();

        narrationVolumeThumb = new Panel
        {
            Size = new Size(14, 18),
            BackColor = Color.FromArgb(235, 246, 255),
            Cursor = Cursors.Hand
        };
        narrationVolumeThumb.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 246, 255)))
                e.Graphics.FillEllipse(b, 0, 2, narrationVolumeThumb.Width - 1, narrationVolumeThumb.Height - 5);
            using (Pen p = new Pen(Color.FromArgb(70, 120, 180), 1f))
                e.Graphics.DrawEllipse(p, 0, 2, narrationVolumeThumb.Width - 1, narrationVolumeThumb.Height - 5);
        };
        narrationVolumeThumb.MouseDown += (s, e) => BeginNarrationSliderDrag(narrationVolumeThumb.Left + e.X);
        narrationVolumeThumb.MouseMove += (s, e) => ContinueNarrationSliderDrag(narrationVolumeThumb.Left + e.X);
        narrationVolumeThumb.MouseUp += (s, e) => EndNarrationSliderDrag();
        narrationVolumeTrack.Controls.Add(narrationVolumeThumb);
        narrationVolumeThumb.BringToFront();

        narrationControlCard.MouseUp += (s, e) => EndNarrationSliderDrag();
        this.MouseUp += (s, e) => EndNarrationSliderDrag();

        narrationPauseButton.Visible = true;
        narrationVolumeLabel.Visible = true;
        narrationVolumeTrack.Visible = true;
        UpdateNarrationSliderVisual();
    }

    private void UpdateNarrationControlsVisibility()
    {
        bool show = (currentScreen == AppScreen.Artifact && artifactID >= 0);
        if (narrationControlCard != null) narrationControlCard.Visible = show;
    }

    private void UpdateLayout()
    {
        width=this.ClientSize.Width; height=this.ClientSize.Height;
        if(faceStatusLabel!=null)  faceStatusLabel.Location=new Point(width-270,10);
        if(pythonStatusLabel!=null) pythonStatusLabel.Location=new Point(width-270,42);
        if (narrationControlCard != null)
        {
            int left = 18;
            int top = Math.Max(72, height - narrationControlCard.Height - 22);
            narrationControlCard.Location = new Point(left, top);
        }
        if (narrationPauseButton != null) narrationPauseButton.Location = new Point(14, 12);
        if (narrationVolumeLabel != null) narrationVolumeLabel.Location = new Point(14, 54);
        if (narrationVolumeTrack != null) narrationVolumeTrack.Location = new Point(14, 80);
        UpdateNarrationSliderVisual();
    }

    private GraphicsPath RoundRect(Rectangle r,int radius)
    {
        var path=new GraphicsPath(); int d=radius*2;
        path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        path.CloseFigure(); return path;
    }

    private void Form_Resized(object sender,EventArgs e){UpdateLayout(); bgCacheW=-1; ClearRotationFrameCache(); /* invalidate bg cache on resize */}

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
        if (mainTimer != null) mainTimer.Stop();
        StopNarrationAudio();
        if (bgCache != null) bgCache.Dispose();
        ClearRotationFrameCache();
        ClearThumbnailCache();
        client.removeTuioListener(this); client.disconnect();
        System.Environment.Exit(0);
    }

    public static void Main(string[] argv)
    {
        int port = argv.Length==1 ? int.Parse(argv[0]) : 3333;
        Application.Run(new TuioDemo(port));
    }
}
