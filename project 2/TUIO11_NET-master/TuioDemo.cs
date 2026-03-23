/*
	TUIO C# Demo - part of the reacTIVision project
	Copyright (c) 2005-2016 Martin Kaltenbrunner <martin@tuio.org>

	This program is free software; you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation; either version 2 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program; if not, write to the Free Software
	Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using TUIO;
using System.Net.Sockets;
using System.Text;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

	public class TuioDemo : Form , TuioListener
	{
    int slideIndex = 0;
    string[] slideImages ;
    SolidBrush cardBsh = new SolidBrush(Color.FromArgb(30, 30, 60)); 
    System.Windows.Forms.Timer tt;
    string uname = "Visitor";
    Image upic = null;
    private TuioClient client;
		private Dictionary<long,TuioObject> objectList;
		private Dictionary<long,TuioCursor> cursorList;
		private Dictionary<long,TuioBlob> blobList;

		public static int width, height;
		private int window_width =  640;
		private int window_height = 480;
		private int window_left = 0;
		private int window_top = 0;
		private int screen_width = Screen.PrimaryScreen.Bounds.Width;
		private int screen_height = Screen.PrimaryScreen.Bounds.Height;

		private bool fullscreen;
		private bool verbose;

		Font font = new Font("Arial", 10.0f);
		SolidBrush fntBrush = new SolidBrush(Color.White);
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0,0,64));
		SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
		SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
		SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
    private Panel pnlCard;
    private Label lblHello;
    private PictureBox pictureBox1;
    private Label lblStatus;
    Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);

		public TuioDemo(int port) {
        System.Timers.Timer slideTimer = new System.Timers.Timer(3000);
        slideTimer.Elapsed += (s, e) => { slideIndex = (slideIndex + 1) % 5; ; Invoke((Action)Invalidate); };
        slideTimer.Start();
        verbose = false;
			fullscreen = false;
			width = window_width;
			height = window_height;


			this.ClientSize = new System.Drawing.Size(width, height);
			this.Name = "TuioDemo";
			this.Text = "TuioDemo";
        this.WindowState = FormWindowState.Maximized;
        this.FormBorderStyle = FormBorderStyle.None;

        this.Closing+=new CancelEventHandler(Form_Closing);
			this.KeyDown+=new KeyEventHandler(Form_KeyDown);

			this.SetStyle( ControlStyles.AllPaintingInWmPaint |
							ControlStyles.UserPaint |
							ControlStyles.DoubleBuffer, true);

			objectList = new Dictionary<long,TuioObject>(128);
			cursorList = new Dictionary<long,TuioCursor>(128);
			blobList   = new Dictionary<long,TuioBlob>(128);
			
			client = new TuioClient(port);
			client.addTuioListener(this);

			client.connect();
			Thread socketThread = new Thread(stream);
			socketThread.IsBackground = true;
			socketThread.Start();//this right here is to recive stuff from our python code: hand gestures and facial recognition
    }

		private void Form_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e) {

 			if ( e.KeyData == Keys.F1) {
	 			if (fullscreen == false) {

					width = screen_width;
					height = screen_height;

					window_left = this.Left;
					window_top = this.Top;

					this.FormBorderStyle = FormBorderStyle.None;
		 			this.Left = 0;
		 			this.Top = 0;
		 			this.Width = screen_width;
		 			this.Height = screen_height;

		 			fullscreen = true;
	 			} else {

					width = window_width;
					height = window_height;

		 			this.FormBorderStyle = FormBorderStyle.Sizable;
		 			this.Left = window_left;
		 			this.Top = window_top;
		 			this.Width = window_width;
		 			this.Height = window_height;

		 			fullscreen = false;
	 			}
 			} else if ( e.KeyData == Keys.Escape) {
				this.Close();

 			} else if ( e.KeyData == Keys.V ) {
 				verbose=!verbose;
 			}

 		}

		private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			client.removeTuioListener(this);

			client.disconnect();
			System.Environment.Exit(0);
		}

		public void addTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Add(o.SessionID,o);
			} if (verbose) Console.WriteLine("add obj "+o.SymbolID+" ("+o.SessionID+") "+o.X+" "+o.Y+" "+o.Angle);
		}

		public void updateTuioObject(TuioObject o) {

			if (verbose) Console.WriteLine("set obj "+o.SymbolID+" "+o.SessionID+" "+o.X+" "+o.Y+" "+o.Angle+" "+o.MotionSpeed+" "+o.RotationSpeed+" "+o.MotionAccel+" "+o.RotationAccel);
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			}
			if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
		}

		public void addTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Add(c.SessionID,c);
			}
			if (verbose) Console.WriteLine("add cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y);
		}

		public void updateTuioCursor(TuioCursor c) {
			if (verbose) Console.WriteLine("set cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y+" "+c.MotionSpeed+" "+c.MotionAccel);
		}

		public void removeTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Remove(c.SessionID);
			}
			if (verbose) Console.WriteLine("del cur "+c.CursorID + " ("+c.SessionID+")");
 		}

		public void addTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Add(b.SessionID,b);
			}
			if (verbose) Console.WriteLine("add blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area);
		}

		public void updateTuioBlob(TuioBlob b) {
		
			if (verbose) Console.WriteLine("set blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area+" "+b.MotionSpeed+" "+b.RotationSpeed+" "+b.MotionAccel+" "+b.RotationAccel);
		}

		public void removeTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Remove(b.SessionID);
			}
			if (verbose) Console.WriteLine("del blb "+b.BlobID + " ("+b.SessionID+")");
		}

		public void refresh(TuioTime frameTime) {
			Invalidate();
		}
    class Client
    {
        int byteCT;
        public NetworkStream stream;
        byte[] sendData;
        public TcpClient client;

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                Console.WriteLine("connection made ! with " + host);
                return true;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine("Connection Failed: " + e.Message);
                return false;
            }
        }

        public string recieveMessage()
        {
            try
            {
                if (stream == null) return null;
                byte[] receiveBuffer = new byte[1024];
                int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
                Console.WriteLine(bytesReceived);
                string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
                Console.WriteLine(data);
                return data;
            }
            catch (System.Exception e)
            {

            }

            return null;
        }

    }
    string msg = "";
	string oldmsg = "";
    int login = 0;
    int page = 0;
    string btStatus = "Waiting...";
    public void stream()
    {
		
        Client c = new Client();
        if (!c.connectToSocket("localhost", 5000))    
        {
            Console.WriteLine("Could not connect.");
            return;
        }
        
        while (true)
        {
            msg = c.recieveMessage();
            if (string.IsNullOrWhiteSpace(msg))
            {
                btStatus = "Waiting...";
                Invoke((Action)(Invalidate));
                continue;
            }
            //MessageBox.Show(msg);
            if (msg == "q")
            {
                c.stream.Close();
                c.client.Close();
                Console.WriteLine("Connection Terminated !");
                break;
            }
            if(login==0)
            {
                bool found = false;
                foreach (var line in System.IO.File.ReadLines("users.csv"))
                {
                    var p = line.Split(',');

                    if (p.Length >= 5 && p[3].Trim().Equals(msg.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        uname = p[0].Trim();
                        upic = Image.FromFile(p[4].Trim());
                        login = 1;
                        btStatus = "Matched";
                        found = true;
                        break;
                    }
                }
                if (!found) // no match found in the csv file
                {
                    btStatus = "No match for this device in the system";
                }
                Invoke((Action)(Invalidate));

            }
           
            else
            {
                if (msg.Trim() == "SwipeRight")
                {
                    page++;
                    if (page > 4) page = 0;
                    if (page == 4 && room == 0) page++; // skip egypt visitor eza enta fe room egypt
                    if (page == 2 && room == 1) page++; // skip china eza enta fe room china
                    if (page == 3 && room == 2) page++; // skip medieval eza enta fe medieval europe
                    
                }
                if (msg.Trim() == "SwipeLeft")
                {
                    page--;
                    if (page < 0) page = 4;
                    if (page == 4 && room == 0) page--; // skip egypt visitor eza enta fe room egypt
                    if (page == 2 && room == 1) page--; // skip china eza enta fe room china
                    if (page == 3 && room == 2) page--; // skip medieval eza enta fe medieval europe
                }
                Invoke((Action)(Invalidate));
            }

            oldmsg = msg;
        }
    }
   

    int room = 0;
    protected override void OnPaintBackground(PaintEventArgs pevent)
		{
			// Getting the graphics object
			Graphics g = pevent.Graphics;
        g.FillRectangle(bgrBrush, new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height));
        //gui for logn fr:

        if (uname == "Visitor")
        {
            int cw = 500, ch = 500;
            int cX = (this.ClientSize.Width - cw) / 2;
            int cY = (this.ClientSize.Height - ch) / 2;
           
            g.FillRectangle(cardBsh, cX, cY, cw, ch);
            SolidBrush avatarBrsh = new SolidBrush(Color.FromArgb(80, 80, 120));
            g.FillEllipse(avatarBrsh, cX + 190, cY + 60, 120, 120);
            Font hellofont = new Font("Arial", 22f, FontStyle.Bold);
            if (upic != null)
                g.DrawImage(upic, cX + 190, cY + 60, 120, 120);
            else
                g.FillEllipse(avatarBrsh, cX + 190, cY + 60, 120, 120);
            g.DrawString("Hello, " + uname, hellofont, fntBrush, cX + 150, cY + 210);
            Font otherfont = new Font("Arial", 13f);
            SolidBrush silverboibush = new SolidBrush(Color.Silver);
            g.DrawString("Bluetooth Verification", otherfont, silverboibush, cX + 140, cY + 270);
            Font statusfont = new Font("Arial", 11f, FontStyle.Italic);
            SolidBrush bluebsh = new SolidBrush(Color.FromArgb(100, 200, 255));
            g.DrawString(btStatus, statusfont, bluebsh, cX + 120, cY + 330);
           
            //end of gui for login
        }
        //gui for home page
        else if(page==0)
        {
          
            int cW = 400, cH = 350, gap = 30;
            int startX = (this.ClientSize.Width - (cW * 3 + gap * 2)) / 2;
            int startY = (this.ClientSize.Height - cH) / 2;

            string[] labels = { "Recommended", "Previously Seen", "Favorites" };
            string[] img_links = { "Untitled.jpg", "Untitled1.jpg", "12.jpg" };

            for (int i = 0; i < 3; i++)
            {
                int x = startX + i * (cW + gap);

              
                g.FillRectangle(cardBsh, x, startY, cW, cH);

         
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), x + 10, startY + 10, cW - 20, cH - 50);
                Image img = Image.FromFile(img_links[i]);
                g.DrawImage(img, x + 10, startY + 10, cW - 20, cH - 50);
         
                g.DrawString(labels[i], new Font("Arial", 13f, FontStyle.Bold), fntBrush,
                             x + 14, startY + cH - 34);
            }

            if (upic != null)
                g.DrawImage(upic, 60, 90, 100, 100);
            else
                g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
        }
        //end of gui for home
        //gui for room page 
        else if (page == 1)
        {
            string img1="";
            string img2="";
            if (room == 0)//egypt
            {
                 img1 = "Untitled.jpg";
                 img2 = "Untitled1.jpg";
            }
            if (room==1)//china
            {
                img1 = "china1.jpg";
                img2 = "china2.jpg";
            }
            if (room==2)//europe
            {
                img1 = "eu1.jpg";
                img2 = "eu2.jpg";
            }
            
                int cW = 400, cH = 350, gap = 30;
                int totalW = cW * 3 + gap * 2;
                int startX = (this.ClientSize.Width - totalW) / 2;
                int startY = (this.ClientSize.Height - cH) / 2;

                // Preview
                g.FillRectangle(cardBsh, startX, startY, cW, cH);
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), startX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawImage(Image.FromFile(img1), startX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawString("Preview", new Font("Arial", 13f, FontStyle.Bold), fntBrush, startX + 14, startY + cH - 34);

                //  SCAN
                int midX = startX + cW + gap;
                g.FillRectangle(cardBsh, midX, startY, cW, cH);
                g.DrawString("SCAN", new Font("Arial", 48f, FontStyle.Bold), fntBrush,
                    midX + (cW / 2) - 70, startY + (cH / 2) - 30);

                //  Suggested
                int rightX = startX + (cW + gap) * 2;
                g.FillRectangle(cardBsh, rightX, startY, cW, cH);
                g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 100)), rightX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawImage(Image.FromFile(img2), rightX + 10, startY + 10, cW - 20, cH - 50);
                g.DrawString("Suggested", new Font("Arial", 13f, FontStyle.Bold), fntBrush, rightX + 14, startY + cH - 34);

                g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
                if (upic != null) g.DrawImage(upic, 60, 90, 100, 100);
                else g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
            
            
        }//end of gui for room
        else if (page == 2 || page == 3||page==4)//gui for vistor of rooms but not in said room
        {
            int cW = 900, cH = 500;
            int cX = (this.ClientSize.Width - cW) / 2;
            int cY = (this.ClientSize.Height - cH) / 2;
            if(page==2)//china
            {
                slideImages = new string[] { "china1.jpg", "china2.jpg", "china3.jpg", "china4.jpg", "china5.jpg" };
            }
            if (page == 3)//medival europe
            {
                slideImages = new string[] { "eu1.jpg", "eu2.jpg", "eu3.jpg", "eu4.jpg", "eu5.jpg" };
            }
            if(page == 4) //egypt if u are not in gyipt
            {
                slideImages = new string[] { "egy1.jpg", "egy2.jpg", "egy3.jpg", "egy4.jpg", "egy5.jpg" };
            }
            g.FillRectangle(cardBsh, cX, cY, cW, cH);
            g.DrawImage(Image.FromFile(slideImages[slideIndex]), cX + 10, cY + 10, cW - 20, cH - 50);
            g.DrawString("News", new Font("Arial", 13f, FontStyle.Bold), fntBrush, cX + 14, cY + cH - 34);

            g.DrawString("Hello, " + uname, new Font("Arial", 20f, FontStyle.Bold), fntBrush, 40, 30);
            if (upic != null) g.DrawImage(upic, 60, 90, 100, 100);
            else g.FillEllipse(new SolidBrush(Color.FromArgb(80, 80, 120)), 60, 90, 100, 100);
        }
        // draw the cursor path
        if (cursorList.Count > 0) {
 			 lock(cursorList) {
			 foreach (TuioCursor tcur in cursorList.Values) {
					List<TuioPoint> path = tcur.Path;
					TuioPoint current_point = path[0];

					for (int i = 0; i < path.Count; i++) {
						TuioPoint next_point = path[i];
						g.DrawLine(curPen, current_point.getScreenX(width), current_point.getScreenY(height), next_point.getScreenX(width), next_point.getScreenY(height));
						current_point = next_point;
					}
					g.FillEllipse(curBrush, current_point.getScreenX(width) - height / 100, current_point.getScreenY(height) - height / 100, height / 50, height / 50);
					g.DrawString(tcur.CursorID + "", font, fntBrush, new PointF(tcur.getScreenX(width) - 10, tcur.getScreenY(height) - 10));
				}
			}
		 }

			// draw the objects
			if (objectList.Count > 0) {
 				lock(objectList) {
					foreach (TuioObject tobj in objectList.Values) {
                    int size = height / 10;
					int ox=0;
					int oy=0;
                    if (tobj.SymbolID == 0)
                    {
                        Image bgimg = Image.FromFile("background1.png");
                        Image objimg = Image.FromFile("assets/objects/obj1.png");
                        ox = tobj.getScreenX(width);
                        oy = tobj.getScreenY(height);
                        g.DrawImage(bgimg, 0, 0, width, height);
                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);
                       

                        g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);

                    }
                    if (tobj.SymbolID == 1)
                    {
                        Image bgimg = Image.FromFile("background2.png");
                        Image objimg = Image.FromFile("assets/objects/obj2.png");
                        ox = tobj.getScreenX(width);
                        oy = tobj.getScreenY(height);
                        g.DrawImage(bgimg, 0, 0, width, height);
                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);


                        g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);

                    }
                    if (tobj.SymbolID == 2)
                    {
                        Image bgimg = Image.FromFile("background3.png");
                        Image objimg = Image.FromFile("assets/objects/obj3.png");
                        ox = tobj.getScreenX(width);
                        oy = tobj.getScreenY(height);
                        g.DrawImage(bgimg, 0, 0, width, height);
                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);


                        g.DrawImage(objimg, ox - size / 2, oy - size / 2, size, size);

                    }
					if(tobj.SymbolID != 0&&tobj.SymbolID!=1&&tobj.SymbolID!=2) //defaultt
					{
                         ox = tobj.getScreenX(width);
                         oy = tobj.getScreenY(height);
                         size = height / 10;

                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);

                        g.FillRectangle(objBrush, new Rectangle(ox - size / 2, oy - size / 2, size, size));
                    }







                    g.TranslateTransform(ox, oy);
						g.RotateTransform(-1 * (float)(tobj.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-ox, -oy);

						g.DrawString(tobj.SymbolID + "", font, fntBrush, new PointF(ox - 10, oy - 10));
					}
				}
			}

			// draw the blobs
			if (blobList.Count > 0) {
				lock(blobList) {
					foreach (TuioBlob tblb in blobList.Values) {
						int bx = tblb.getScreenX(width);
						int by = tblb.getScreenY(height);
						float bw = tblb.Width*width;
						float bh = tblb.Height*height;

						g.TranslateTransform(bx, by);
						g.RotateTransform((float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);

						g.FillEllipse(blbBrush, bx - bw / 2, by - bh / 2, bw, bh);

						g.TranslateTransform(bx, by);
						g.RotateTransform(-1 * (float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);
						
						g.DrawString(tblb.BlobID + "", font, fntBrush, new PointF(bx, by));
					}
				}
			}
		}

    private void InitializeComponent()
    {
            this.pnlCard = new System.Windows.Forms.Panel();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.lblHello = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.pnlCard.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlCard
            // 
            this.pnlCard.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(60)))));
            this.pnlCard.Controls.Add(this.lblStatus);
            this.pnlCard.Controls.Add(this.lblHello);
            this.pnlCard.Controls.Add(this.pictureBox1);
            this.pnlCard.Location = new System.Drawing.Point(257, 105);
            this.pnlCard.Name = "pnlCard";
            this.pnlCard.Size = new System.Drawing.Size(500, 500);
            this.pnlCard.TabIndex = 0;
            this.pnlCard.Paint += new System.Windows.Forms.PaintEventHandler(this.panel1_Paint);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(190, 101);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(120, 120);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // lblHello
            // 
            this.lblHello.AutoSize = true;
            this.lblHello.Font = new System.Drawing.Font("Arial", 22F);
            this.lblHello.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblHello.Location = new System.Drawing.Point(149, 254);
            this.lblHello.Name = "lblHello";
            this.lblHello.Size = new System.Drawing.Size(219, 42);
            this.lblHello.TabIndex = 1;
            this.lblHello.Text = "Hello, Visitor";
            this.lblHello.Click += new System.EventHandler(this.lblHello_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Arial", 18F);
            this.lblStatus.ForeColor = System.Drawing.Color.Cornsilk;
            this.lblStatus.Location = new System.Drawing.Point(184, 319);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(140, 35);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "Waiting...";
            this.lblStatus.Click += new System.EventHandler(this.label1_Click);
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(1564, 743);
            this.Controls.Add(this.pnlCard);
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.pnlCard.ResumeLayout(false);
            this.pnlCard.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

    }
	 
    private void TuioDemo_Load(object sender, EventArgs e)
    {
        pnlCard.Left = (this.ClientSize.Width - pnlCard.Width) / 2;
        pnlCard.Top = (this.ClientSize.Height - pnlCard.Height) / 2;
    }

    private void panel1_Paint(object sender, PaintEventArgs e)
    {

    }

    private void lblHello_Click(object sender, EventArgs e)
    {

    }

    private void label1_Click(object sender, EventArgs e)
    {

    }

    public static void Main(String[] argv) {
	 		int port = 0;
			switch (argv.Length) {
				case 1:
					port = int.Parse(argv[0],null);
					if(port==0) goto default;
					break;
				case 0:
					port = 3333;
					break;
				default:
					Console.WriteLine("usage: mono TuioDemo [port]");
					System.Environment.Exit(0);
					break;
			}
			
			TuioDemo app = new TuioDemo(port);
			Application.Run(app);
		}
	}
