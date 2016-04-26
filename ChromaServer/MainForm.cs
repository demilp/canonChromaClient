using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using SIA;
using ChromaClient.Properties;
using System.Configuration;
using System.Threading;
using ChromaServer.Camera.Canon;
using Canon.Eos.Framework.Eventing;
using Canon.Eos.Framework;
using System.Drawing.Text;
using Bypass;
using System.Drawing.Imaging;

namespace ChromaClient
{
    public partial class MainForm : Form
    {

        private Configuration _settings = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        //Process      
        Bitmap _backgroundImage; //Imagen de fondo
        Bitmap _userImage; //Imagen de la persona
        Bitmap _foregroundImage; //Imagen de fondo

        Bitmap _finalImage; //Imagen con el chroma hecho
        Chromagic.ChromaKey _chromagic;
        private Int32 _timeSleep;

        private FileSystemWatcher watcher;

        //Photo
        private PhotoInfo photoInfo = null;
        private Int32 _photoWidth;
        private Int32 _photoHeight;

        //Paths
        private String PHOTOS_PROCESSED_PATH = Application.StartupPath + @"\processed\";
        private String CAMERA_DUMP_PATH = Application.StartupPath + @"\cameraDump\";
        private String PHOTOS_BACKUP_PATH = Application.StartupPath + @"\originalsBackup\";
        private String CAMERA_CALIBRATION = Application.StartupPath + @"\cameraCalibration\";

        //Config
        private bool calibrating = false;

        //Server
        private Bypass.BypassClient _server;

        //Crop Image Config
        private Rectangle _rectCrop;
        private Bitmap _cropUserBitmap;

        private decimal _cropUserX;
        private decimal _cropUserY;
        private decimal _cropUserWidth;
        private decimal _cropUserHeight;

        //Chroma Image Config
        private Bitmap _chromaConfigBitmap;

        private Int32 _hue;
        private Int32 _tolerance;
        private Int32 _saturation;
        private Int32 _minValue;
        private Int32 _maxValue;

        //Position
        private Bitmap _combinedConfigBitmap;
        private Bitmap _positionConfigBitmap;
        private Bitmap _backgroundConfigBitmap;

        private Int32 _picX;
        private Int32 _picY;
        private Decimal _picScale;
        private Effects _picEffect;
        private enum Effects { NONE, GRAYSCALE, SEPIA }

        //Camera
        private readonly CanonFrameworkManager _manager;

        //Fonts
        private string _fontFamilyName;
        private float _fontSize1;
        private Int32 _positionXFont1;
        private Int32 _positionYFont1;
        private Int32 _colorFont1;
        private string _aligmentFont1 = "Left";
        private float _fontSize2;
        private Int32 _positionXFont2;
        private Int32 _positionYFont2;
        private Int32 _colorFont2;
        private string _aligmentFont2 = "Left";
        private FontFamily[] fontFamilies;

        private delegate void ThisDelegate(string pMessage);

        ColorMatrix sepiaMatrix = new ColorMatrix(
        new float[][]
        {
                new float[]{.393f, .349f, .272f, 0, 0},
                new float[]{.769f, .686f, .534f, 0, 0},
                new float[]{.189f, .168f, .131f, 0, 0},
                new float[]{0, 0, 0, 1, 0},
                new float[]{0, 0, 0, 0, 1}
            });

        ColorMatrix grayscaleMatrix = new ColorMatrix(
        new float[][]
        {
            new float[] {.3f, .3f, .3f, 0, 0},
            new float[] {.59f, .59f, .59f, 0, 0},
            new float[] {.11f, .11f, .11f, 0, 0},
            new float[] {0, 0, 0, 1, 0},
            new float[] {0, 0, 0, 0, 1}
        });

        public MainForm()
        {
            InitializeComponent();

            if (!Directory.Exists(PHOTOS_PROCESSED_PATH))
                Directory.CreateDirectory(PHOTOS_PROCESSED_PATH);

            if (!Directory.Exists(CAMERA_DUMP_PATH))
                Directory.CreateDirectory(CAMERA_DUMP_PATH);

            if (!Directory.Exists(CAMERA_CALIBRATION))
                Directory.CreateDirectory(CAMERA_CALIBRATION);

            if (!Directory.Exists(PHOTOS_BACKUP_PATH))
                Directory.CreateDirectory(PHOTOS_BACKUP_PATH);

            if (_settings.AppSettings.Settings["CameraSourcePath"].Value != "")
                CAMERA_DUMP_PATH = _settings.AppSettings.Settings["CameraSourcePath"].Value;

            if (!Directory.Exists(CAMERA_DUMP_PATH))
            {
                MessageBox.Show("Check config file");
                Environment.Exit(0);
                return;
            }

            _timeSleep = Int32.Parse(_settings.AppSettings.Settings["TimeWaitBeforePhotoProcess"].Value);

            _chromagic = new Chromagic.ChromaKey();

            photoInfo = new PhotoInfo("test2", Application.StartupPath + @"\testMode\bgtest.jpg", Application.StartupPath + @"\testMode\fgtest.png", "description1", "description2");

            loadBackground(photoInfo.BackgroundPath);
            loadForeground(photoInfo.ForegroundPath);

            watcher = new FileSystemWatcher();
            watcher.Path = CAMERA_DUMP_PATH;
            watcher.Filter = "*.jpg";
            watcher.Created += new FileSystemEventHandler(watcher_Created);
            watcher.Renamed += new RenamedEventHandler(watcher_Renamed);
            watcher.EnableRaisingEvents = true;

            _server = new Bypass.BypassClient(ConfigurationManager.AppSettings["ServerIp"], int.Parse(ConfigurationManager.AppSettings["ServerPort"]), ConfigurationManager.AppSettings["ServerDelimiter"], "chroma", "tool");
            _server.CommandReceivedEvent += server_MessageReceivedEvent;

            _manager = new CanonFrameworkManager();
            _manager.CameraAdded += this.HandleCameraAdded;

            this.configurationControl.Visible = false;
            this.saveConfigBtn.Visible = false;
            this.processPanel.Visible = true;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            loadSettings();
            StartUpCamera();
            LoadCameras();
        }

        private void loadSettings()
        {
            //Crop
            _cropUserX = Decimal.Parse(_settings.AppSettings.Settings["CropUserX"].Value);
            _cropUserY = Decimal.Parse(_settings.AppSettings.Settings["CropUserY"].Value);
            _cropUserWidth = Decimal.Parse(_settings.AppSettings.Settings["CropUserWidth"].Value);
            _cropUserHeight = Decimal.Parse(_settings.AppSettings.Settings["CropUserHeight"].Value);

            numericXCrop.Value = _cropUserX;
            numericYCrop.Value = _cropUserY;
            numericWidthCrop.Value = _cropUserWidth;
            numericHeightCrop.Value = _cropUserHeight;

            //Chroma
            _hue = Int32.Parse(_settings.AppSettings.Settings["ChromaHue"].Value);
            _tolerance = Int32.Parse(_settings.AppSettings.Settings["ChromaTolerance"].Value);
            _saturation = Int32.Parse(_settings.AppSettings.Settings["ChromaSaturation"].Value);
            _minValue = Int32.Parse(_settings.AppSettings.Settings["ChromaMinValue"].Value);
            _maxValue = Int32.Parse(_settings.AppSettings.Settings["ChromaMaxValue"].Value);

            _chromagic.Hue = (float)_hue;
            _chromagic.Tolerance = (float)_tolerance;
            _chromagic.Saturation = (float)_saturation / 100.0f;
            _chromagic.MinValue = (float)_minValue / 100.0f;
            _chromagic.MaxValue = (float)_maxValue / 100.0f;

            numericHue.Value = _hue;
            numericTolerance.Value = _tolerance;
            numericSaturation.Value = _saturation;
            numericMinValueChroma.Value = _minValue;
            numericMaxValueChroma.Value = _maxValue;

            //Position
            _photoWidth = Int32.Parse(_settings.AppSettings.Settings["PhotoWidth"].Value);
            _photoHeight = Int32.Parse(_settings.AppSettings.Settings["PhotoHeight"].Value);
            _picX = Int32.Parse(_settings.AppSettings.Settings["PicPositionX"].Value);
            _picY = Int32.Parse(_settings.AppSettings.Settings["PicPositionY"].Value);
            _picScale = Decimal.Parse(_settings.AppSettings.Settings["PicScale"].Value);
            _picEffect = _settings.AppSettings.Settings["PicEffect"].Value == "Grayscale" ? Effects.GRAYSCALE : _settings.AppSettings.Settings["PicEffect"].Value == "Sepia" ? Effects.SEPIA : Effects.NONE;

            numericWidthPhoto.Value = _photoWidth;
            numericHeightPhoto.Value = _photoHeight;
            numericPositionX.Value = _picX;
            numericPositionY.Value = _picY;
            numericScale.Value = _picScale;

            //Backgrounds
            numericPosXBackground.Value = _picX;
            numericPosYBackground.Value = _picY;
            numericScaleBackground.Value = _picScale;

            string searchPattern = "PicPositionX_";

            foreach (string key in _settings.AppSettings.Settings.AllKeys)
            {
                if (key.IndexOf(searchPattern) == 0)
                {
                    string id = key.Substring(searchPattern.Length);

                    string[] details = new string[] { id, _settings.AppSettings.Settings[key].Value, _settings.AppSettings.Settings["PicPositionY_" + id].Value, _settings.AppSettings.Settings["PicScale_" + id].Value, _settings.AppSettings.Settings["PicEffect_" + id].Value };
                    ListViewItem itemToSave = new ListViewItem(details);
                    backgroundsList.Items.Add(itemToSave);
                }
            }

            //Fonts
            _fontFamilyName = _settings.AppSettings.Settings["FontFamilyName"].Value;

            InstalledFontCollection installedFontCollection = new InstalledFontCollection();
            fontFamilies = installedFontCollection.Families;
            fontsComboBox.DataSource = fontFamilies;
            fontsComboBox.DisplayMember = "Name";
            fontsComboBox.SelectedIndex = fontsComboBox.FindStringExact("Arial");
            _fontFamilyName = (fontsComboBox.SelectedItem as FontFamily).Name;

            _fontSize1 = float.Parse(_settings.AppSettings.Settings["FontSize1"].Value);
            numericFontSize1.Value = (Decimal)_fontSize1;
            _positionXFont1 = Int32.Parse(_settings.AppSettings.Settings["PositionXFont1"].Value);
            numericPositionXFont1.Value = _positionXFont1;
            _positionYFont1 = Int32.Parse(_settings.AppSettings.Settings["PositionYFont1"].Value);
            numericPositionYFont1.Value = _positionYFont1;
            _colorFont1 = Int32.Parse(_settings.AppSettings.Settings["ColorFont1"].Value);
            textBoxColorFont1.BackColor = Color.FromArgb(_colorFont1);
            _aligmentFont1 = _settings.AppSettings.Settings["AligmentFont1"].Value;
            if (_aligmentFont1 == "Center")
                radioButtonCenter1.Checked = true;
            else if (_aligmentFont1 == "Right")
                radioButtonRight1.Checked = true;
            else
                radioButtonLeft1.Checked = true;

            _fontSize2 = float.Parse(_settings.AppSettings.Settings["FontSize2"].Value);
            numericFontSize2.Value = (Decimal)_fontSize2;
            _positionXFont2 = Int32.Parse(_settings.AppSettings.Settings["PositionXFont2"].Value);
            numericPositionXFont2.Value = _positionXFont2;
            _positionYFont2 = Int32.Parse(_settings.AppSettings.Settings["PositionYFont2"].Value);
            numericPositionYFont2.Value = _positionYFont2;
            _colorFont2 = Int32.Parse(_settings.AppSettings.Settings["ColorFont2"].Value);
            textBoxColorFont2.BackColor = Color.FromArgb(_colorFont2);
            _aligmentFont2 = _settings.AppSettings.Settings["AligmentFont2"].Value;
            if (_aligmentFont2 == "Center")
                radioButtonCenter2.Checked = true;
            else if (_aligmentFont2 == "Right")
                radioButtonRight2.Checked = true;
            else
                radioButtonLeft2.Checked = true;

            //General
            sourceFolderText.Text = _settings.AppSettings.Settings["CameraSourcePath"].Value;
            outputFolderText.Text = _settings.AppSettings.Settings["PhotoDestPath"].Value;

        }

        private void processingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.configurationControl.Visible = false;
            this.saveConfigBtn.Visible = false;
            this.processPanel.Visible = true;

            watcher.Created += new FileSystemEventHandler(watcher_Created);
            watcher.Renamed += new RenamedEventHandler(watcher_Renamed);
        }

        private void configurationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.processPanel.Visible = false;
            this.configurationControl.Visible = true;
            this.saveConfigBtn.Visible = true;

            watcher.Created -= new FileSystemEventHandler(watcher_Created);
            watcher.Renamed -= new RenamedEventHandler(watcher_Renamed);
        }

        private void configurationControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (configurationControl.SelectedTab == chromaTab)
            {
                applyChroma();
            }
        }

        void watcher_Renamed(object sender, RenamedEventArgs e)
        {
            Thread.Sleep(_timeSleep);
            processPicture(e.FullPath);
        }

        void watcher_Created(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(_timeSleep);
            processPicture(e.FullPath);
        }

        void server_MessageReceivedEvent(object sender, CommandEventArgs e)
        {
            this.Invoke(new ThisDelegate(a =>
            {
                debugLabel.Text = a;
            }), "Message Received");


            this.Invoke(new ThisDelegate(a =>
            {
                debugLabel.Text = a;
            }), "TCP: " + e.command);

            char[] splitter = { '|' };
            string[] parameters = e.command.Split(splitter);

            try
            {
                switch (parameters.Length)
                {
                    case 1:
                        if (parameters[0] == "takephoto")
                        {
                            TakePhotoTCP(false);
                        }
                        break;
                    case 2:
                        photoInfo = new PhotoInfo(parameters[0], parameters[1], "", "", "");
                        break;
                    case 3:
                        photoInfo = new PhotoInfo(parameters[0], parameters[1], parameters[2], "", "");
                        break;
                    case 4:
                        photoInfo = new PhotoInfo(parameters[0], parameters[1], parameters[2], parameters[3], "");
                        break;
                    case 5:
                        photoInfo = new PhotoInfo(parameters[0], parameters[1], parameters[2], parameters[3], parameters[4]);
                        break;
                }

            }
            catch (Exception _e)
            {
                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), _e.Message);
            }

            try
            {
                loadBackground(photoInfo.BackgroundPath);
                loadForeground(photoInfo.ForegroundPath);
            }
            catch (Exception _e)
            {
                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), _e.Message);
            }
        }

        private void TakePhotoTCP(bool calibrating)
        {
            this.SafeCall(() =>
            {
                var camera = this.GetSelectedCamera();
                if (camera != null)
                {
                    if (calibrating)
                        camera.SavePicturesToHost(CAMERA_CALIBRATION);
                    else
                        camera.SavePicturesToHost(CAMERA_DUMP_PATH);

                    camera.TakePicture();
                }

            }, ex => MessageBox.Show(ex.ToString(), Resources.TakePictureError, MessageBoxButtons.OK, MessageBoxIcon.Error));
        }

        private void loadBackground(String bg)
        {
            if (_backgroundImage != null)
            {
                _backgroundImage.Dispose();
            }

            if (bg != "")
            {
                _backgroundImage = new Bitmap(bg);
                backgroundPicture.Image = _backgroundImage;
            }
        }

        private void loadUserPicture(String user)
        {
            _userImage = new Bitmap(user);
            userPicture.Image = _userImage;
        }

        private void loadForeground(String fg)
        {
            if (_foregroundImage != null)
            {
                _foregroundImage.Dispose();
            }

            if (fg != "")
            {
                _foregroundImage = new Bitmap(fg);
                foregroundPicture.Image = _foregroundImage;
            }
            else
            {
                _foregroundImage = new Bitmap(Application.StartupPath + @"\testMode\empty.png");
                foregroundPicture.Image = _foregroundImage;
            }
        }

        private void processPicture(String filename)
        {
            // If not under DEBUG and not TCP message arrived, we erase the photo
            if (photoInfo == null)
            {
                File.Delete(filename);
                return;
            }

            try
            {
                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), "Processing new photo");

                Int64 picCount = Directory.GetFiles(PHOTOS_BACKUP_PATH, "*.*", SearchOption.TopDirectoryOnly).Length;
                String finalPath = PHOTOS_BACKUP_PATH + (picCount + 1) + ".jpg";

                if (File.Exists(finalPath))
                    File.Delete(finalPath);

                File.Move(filename, finalPath);

                loadUserPicture(finalPath);

                if (_finalImage != null)
                    _finalImage.Dispose();

                compositeImages();

                _finalImage.Save(PHOTOS_PROCESSED_PATH + (picCount + 1) + ".jpg", System.Drawing.Imaging.ImageFormat.Jpeg);

                if (_settings.AppSettings.Settings["PhotoDestPath"].Value != "")
                    File.Copy(PHOTOS_PROCESSED_PATH + (picCount + 1) + ".jpg", _settings.AppSettings.Settings["PhotoDestPath"].Value + "\\" + (picCount + 1) + ".jpg");

                processedPictureBox.Image = Image.FromFile(PHOTOS_PROCESSED_PATH + (picCount + 1) + ".jpg");

                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), "Photo processed");
            }
            catch (Exception e)
            {
                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), e.Message);

            }
        }

        private void compositeImages()
        {
            if (_backgroundImage == null)
                return;

            if (_userImage == null)
                return;

            _finalImage = new Bitmap(_photoWidth, _photoHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics finalGraph = Graphics.FromImage(_finalImage);
            finalGraph.DrawImage(_backgroundImage, new Rectangle(0, 0, _photoWidth, _photoHeight));

            _chromagic.Hue = (float)_hue;
            _chromagic.Tolerance = (float)_tolerance;
            _chromagic.Saturation = (float)_saturation / 100.0f;
            _chromagic.MinValue = (float)_minValue / 100.0f;
            _chromagic.MaxValue = (float)_maxValue / 100.0f;

            Bitmap chroma = new Bitmap(_photoWidth, _photoHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics chromaGraph = Graphics.FromImage(chroma);

            int currentPicX = _picX;
            int currentPicY = _picY;
            decimal currentScale = _picScale;
            Effects currentEffect = _picEffect;

            if (_settings.AppSettings.Settings["PicPositionX_" + photoInfo.Id] != null)
                currentPicX = Int32.Parse(_settings.AppSettings.Settings["PicPositionX_" + photoInfo.Id].Value);

            if (_settings.AppSettings.Settings["PicPositionY_" + photoInfo.Id] != null)
                currentPicY = Int32.Parse(_settings.AppSettings.Settings["PicPositionY_" + photoInfo.Id].Value);
            string ef = _settings.AppSettings.Settings["PicEffect_" + photoInfo.Id].Value;
            if (ef != null)
                currentEffect = ef == "Grayscale" ? Effects.GRAYSCALE : ef == "Sepia" ? Effects.SEPIA : Effects.NONE;

            chromaGraph.DrawImage((Bitmap)_userImage, new Rectangle(currentPicX, currentPicY, (int)(_userImage.Width * _cropUserWidth * currentScale / 100), (int)(_userImage.Height * _cropUserHeight * currentScale / 100)), new Rectangle((int)_cropUserX * _userImage.Width / 100, (int)_cropUserY * _userImage.Height / 100, (int)_cropUserWidth * _userImage.Width / 100, (int)_cropUserHeight * _userImage.Height / 100), GraphicsUnit.Pixel);


            this.Invoke(new ThisDelegate(a =>
            {
                debugLabel.Text = currentScale.ToString();
            }), "");
            _chromagic.Chroma(chroma);

            if (currentEffect != Effects.NONE)
            {
                //create some image attributes
                ImageAttributes attributes = new ImageAttributes();
                if (currentEffect == Effects.GRAYSCALE)
                {
                    //set the color matrix attribute
                    attributes.SetColorMatrix(grayscaleMatrix);
                }
                else
                {
                    //set the color matrix attribute
                    attributes.SetColorMatrix(sepiaMatrix);
                }
                Bitmap newBitmap = new Bitmap(chroma.Width, chroma.Height);
                Graphics g = Graphics.FromImage(newBitmap);
                g.DrawImage(chroma, new Rectangle(0, 0, chroma.Width, chroma.Height),
      0, 0, chroma.Width, chroma.Height, GraphicsUnit.Pixel, attributes);
                g.Dispose();
                chroma = newBitmap;

            }

            finalGraph.DrawImage(chroma, new Rectangle(0, 0, _photoWidth, _photoHeight));

            //Apply Fonts
            if (photoInfo != null && (photoInfo.Description1 != "" || photoInfo.Description2 != ""))
            {

                try
                {
                    if (photoInfo.Description1 != "")
                    {
                        Font font1 = new Font(_settings.AppSettings.Settings["FontFamilyName"].Value, float.Parse(_settings.AppSettings.Settings["FontSize1"].Value), FontStyle.Bold, GraphicsUnit.Pixel);
                        Color color = Color.FromArgb(Int32.Parse(_settings.AppSettings.Settings["ColorFont1"].Value));
                        SolidBrush brush = new SolidBrush(color);
                        StringFormat sf = new StringFormat();

                        if (_settings.AppSettings.Settings["AligmentFont1"].Value == "Center")
                            sf.Alignment = StringAlignment.Center;
                        else if (_settings.AppSettings.Settings["AligmentFont1"].Value == "Right")
                            sf.Alignment = StringAlignment.Far;
                        else
                            sf.Alignment = StringAlignment.Near;

                        Rectangle atpoint1 = new Rectangle(new Point(Int32.Parse(_settings.AppSettings.Settings["PositionXFont1"].Value), Int32.Parse(_settings.AppSettings.Settings["PositionYFont1"].Value)), new Size(_photoWidth, (int)float.Parse(_settings.AppSettings.Settings["FontSize1"].Value) + 20));
                        finalGraph.DrawString(photoInfo.Description1, font1, brush, atpoint1, sf);
                    }

                    if (photoInfo.Description2 != "")
                    {
                        Font font2 = new Font(_settings.AppSettings.Settings["FontFamilyName"].Value, float.Parse(_settings.AppSettings.Settings["FontSize2"].Value), FontStyle.Bold, GraphicsUnit.Pixel);
                        Color color2 = Color.FromArgb(Int32.Parse(_settings.AppSettings.Settings["ColorFont2"].Value));
                        SolidBrush brush2 = new SolidBrush(color2);
                        StringFormat sf2 = new StringFormat();

                        if (_settings.AppSettings.Settings["AligmentFont2"].Value == "Center")
                            sf2.Alignment = StringAlignment.Center;
                        else if (_settings.AppSettings.Settings["AligmentFont2"].Value == "Right")
                            sf2.Alignment = StringAlignment.Far;
                        else
                            sf2.Alignment = StringAlignment.Near;

                        Rectangle atpoint2 = new Rectangle(new Point(Int32.Parse(_settings.AppSettings.Settings["PositionXFont2"].Value), Int32.Parse(_settings.AppSettings.Settings["PositionYFont2"].Value)), new Size(_photoWidth, (int)float.Parse(_settings.AppSettings.Settings["FontSize2"].Value) + 20));
                        finalGraph.DrawString(photoInfo.Description2, font2, brush2, atpoint2, sf2);
                    }

                }
                catch (Exception exception)
                {
                    //MessageBox.Show(exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            if (_foregroundImage != null)
            {
                finalGraph.DrawImage(_foregroundImage, 0, 0, _photoWidth, _photoHeight);
            }

        }

        private void Form1_FormClosed_1(object sender, FormClosedEventArgs e)
        {
            _manager.ReleaseFramework();
            _server.Close();

            watcher.Created -= new FileSystemEventHandler(watcher_Created);
            watcher.Renamed -= new RenamedEventHandler(watcher_Renamed);
            watcher.Dispose();

            Application.Exit();
        }

        //CROP
        private void openUserPictureBtn_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog open = new OpenFileDialog();
                open.Filter = "Image Files(*.jpg; *.jpeg; *.gif; *.bmp)|*.jpg; *.jpeg; *.gif; *.bmp";
                if (open.ShowDialog() == DialogResult.OK)
                {
                    _cropUserBitmap = new Bitmap(open.FileName);
                    userCropPictureBox.Image = (Bitmap)_cropUserBitmap.Clone();
                }

                open.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed loading image");
            }

        }

        private void userCropPictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            _rectCrop = new Rectangle(e.X, e.Y, 0, 0);
            userCropPictureBox.Invalidate();
        }

        private void userCropPictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _rectCrop = new Rectangle(_rectCrop.X, _rectCrop.Y, e.X - _rectCrop.X, e.Y - _rectCrop.Y);
                userCropPictureBox.Invalidate();
            }
        }

        private void userCropPictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            _rectCrop = new Rectangle(_rectCrop.X, _rectCrop.Y, e.X - _rectCrop.X, e.Y - _rectCrop.Y);
            userCropPictureBox.Invalidate();

            _cropUserX = _rectCrop.X * 100 / userCropPictureBox.Width;
            if (_cropUserX < 0)
                _cropUserX = 0;
            else if (_cropUserX > 100)
                _cropUserX = 100;

            _cropUserY = _rectCrop.Y * 100 / userCropPictureBox.Height;
            if (_cropUserY < 0)
                _cropUserY = 0;
            else if (_cropUserY > 100)
                _cropUserY = 100;

            _cropUserWidth = _rectCrop.Width * 100 / userCropPictureBox.Width;
            if (_cropUserWidth < 0)
                _cropUserWidth = 0;
            else if (_cropUserWidth > 100)
                _cropUserWidth = 100;

            _cropUserHeight = _rectCrop.Height * 100 / userCropPictureBox.Height;
            if (_cropUserHeight < 0)
                _cropUserHeight = 0;
            else if (_cropUserHeight > 100)
                _cropUserHeight = 100;

            numericXCrop.Value = _cropUserX;
            numericYCrop.Value = _cropUserY;
            numericWidthCrop.Value = _cropUserWidth;
            numericHeightCrop.Value = _cropUserHeight;

        }

        private void userCropPictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (userCropPictureBox.Image == null)
                return;

            Pen myPen = new Pen(System.Drawing.Color.Black, 2);
            Brush myBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            Rectangle myRectangle = new Rectangle(_rectCrop.X, _rectCrop.Y, _rectCrop.Width, _rectCrop.Height);

            e.Graphics.DrawRectangle(myPen, myRectangle);
            e.Graphics.FillRectangle(myBrush, myRectangle);
        }

        private void numericXCrop_ValueChanged(object sender, EventArgs e)
        {
            _cropUserX = numericXCrop.Value;
            _rectCrop.X = (int)_cropUserX * userCropPictureBox.Width / 100;
            userCropPictureBox.Invalidate();
        }

        private void numericYCrop_ValueChanged(object sender, EventArgs e)
        {
            _cropUserY = numericYCrop.Value;
            _rectCrop.Y = (int)_cropUserY * userCropPictureBox.Height / 100;
            userCropPictureBox.Invalidate();
        }

        private void numericWidthCrop_ValueChanged(object sender, EventArgs e)
        {
            _cropUserWidth = numericWidthCrop.Value;
            _rectCrop.Width = (int)_cropUserWidth * userCropPictureBox.Width / 100;
            userCropPictureBox.Invalidate();
        }

        private void numericHeightCrop_ValueChanged(object sender, EventArgs e)
        {
            _cropUserHeight = numericHeightCrop.Value;
            _rectCrop.Height = (int)_cropUserHeight * userCropPictureBox.Height / 100;
            userCropPictureBox.Invalidate();
        }

        //CHROMA
        private void applyChroma()
        {
            if (userCropPictureBox.Image == null)
                return;

            _chromagic.Hue = (float)_hue;
            _chromagic.Tolerance = (float)_tolerance;
            _chromagic.Saturation = (float)_saturation / 100.0f;
            _chromagic.MinValue = (float)_minValue / 100.0f;
            _chromagic.MaxValue = (float)_maxValue / 100.0f;

            _chromaConfigBitmap = new Bitmap(chromaPictureBox.Width, chromaPictureBox.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics chromaG = Graphics.FromImage(_chromaConfigBitmap);

            chromaG.DrawImage((Bitmap)_cropUserBitmap.Clone(), new Rectangle(_rectCrop.X, _rectCrop.Y, _rectCrop.Width, _rectCrop.Height), new Rectangle((int)_cropUserX * _cropUserBitmap.Width / 100, (int)_cropUserY * _cropUserBitmap.Height / 100, (int)_cropUserWidth * _cropUserBitmap.Width / 100, (int)_cropUserHeight * _cropUserBitmap.Height / 100), GraphicsUnit.Pixel);
            _chromagic.Chroma(_chromaConfigBitmap);

            chromaPictureBox.Image = _chromaConfigBitmap;
        }

        private void numericHue_ValueChanged(object sender, EventArgs e)
        {
            _hue = (int)numericHue.Value;
            applyChroma();
        }

        private void numericTolerance_ValueChanged(object sender, EventArgs e)
        {
            _tolerance = (int)numericTolerance.Value;
            applyChroma();
        }

        private void numericSaturation_ValueChanged(object sender, EventArgs e)
        {
            _saturation = (int)numericSaturation.Value;
            applyChroma();
        }

        private void numericMinValueChroma_ValueChanged(object sender, EventArgs e)
        {
            _minValue = (int)numericMinValueChroma.Value;
            applyChroma();
        }

        private void numericMaxValueChroma_ValueChanged(object sender, EventArgs e)
        {
            _maxValue = (int)numericMaxValueChroma.Value;
            applyChroma();
        }

        //Position

        private void numericWidthPhoto_ValueChanged(object sender, EventArgs e)
        {
            _photoWidth = (int)numericWidthPhoto.Value;
            applyPosition();
        }

        private void numericHeightPhoto_ValueChanged(object sender, EventArgs e)
        {
            _photoHeight = (int)numericHeightPhoto.Value;
            applyPosition();
        }

        private void applyPosition()
        {
            if (userCropPictureBox.Image == null)
                return;

            if (_backgroundConfigBitmap == null)
                return;

            _combinedConfigBitmap = new Bitmap(_photoWidth, _photoHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics combinedGraph = Graphics.FromImage(_combinedConfigBitmap);
            combinedGraph.DrawImage(_backgroundConfigBitmap, new Rectangle(0, 0, _combinedConfigBitmap.Width, _combinedConfigBitmap.Height));

            _chromagic.Hue = (float)_hue;
            _chromagic.Tolerance = (float)_tolerance;
            _chromagic.Saturation = (float)_saturation / 100.0f;
            _chromagic.MinValue = (float)_minValue / 100.0f;
            _chromagic.MaxValue = (float)_maxValue / 100.0f;

            _positionConfigBitmap = new Bitmap(_photoWidth, _photoHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics positionGraph = Graphics.FromImage(_positionConfigBitmap);

            positionGraph.DrawImage((Bitmap)_cropUserBitmap.Clone(), new Rectangle(_picX, _picY, (int)(_cropUserBitmap.Width * _cropUserWidth * _picScale / 100), (int)(_cropUserBitmap.Height * _cropUserHeight * _picScale / 100)), new Rectangle((int)_cropUserX * _cropUserBitmap.Width / 100, (int)_cropUserY * _cropUserBitmap.Height / 100, (int)_cropUserWidth * _cropUserBitmap.Width / 100, (int)_cropUserHeight * _cropUserBitmap.Height / 100), GraphicsUnit.Pixel);
            _chromagic.Chroma(_positionConfigBitmap);

            combinedGraph.DrawImage(_positionConfigBitmap, new Rectangle(0, 0, _photoWidth, _photoHeight));

            positionPictureBox.Image = _combinedConfigBitmap;

            textoPictureBox.Image = (Bitmap)_combinedConfigBitmap.Clone();
        }

        private void openBackgroundBtn_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog open = new OpenFileDialog();
                open.Filter = "Image Files(*.jpg; *.jpeg; *.gif; *.bmp)|*.jpg; *.jpeg; *.gif; *.bmp";
                if (open.ShowDialog() == DialogResult.OK)
                {
                    _backgroundConfigBitmap = new Bitmap(open.FileName);
                    applyPosition();
                }

                open.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed loading image");
            }
        }

        private void applyPositionBtn_Click(object sender, EventArgs e)
        {
            applyPosition();
        }

        private void numericScale_ValueChanged(object sender, EventArgs e)
        {
            _picScale = numericScale.Value;
            applyPosition();
        }

        private void numericPositionX_ValueChanged(object sender, EventArgs e)
        {
            _picX = (int)numericPositionX.Value;
            applyPosition();
        }

        private void numericPositionY_ValueChanged(object sender, EventArgs e)
        {
            _picY = (int)numericPositionY.Value;
            applyPosition();
        }

        private void fontsComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            _fontFamilyName = (fontsComboBox.SelectedItem as FontFamily).Name;
            applyFont();
        }

        private void descriptionOneTxt_TextChanged(object sender, EventArgs e)
        {
            applyFont();
        }

        private void numericFontSize_ValueChanged(object sender, EventArgs e)
        {
            _fontSize1 = (float)numericFontSize1.Value;
            applyFont();
        }

        private void numericPositionXFont1_ValueChanged(object sender, EventArgs e)
        {
            _positionXFont1 = (int)numericPositionXFont1.Value;
            applyFont();
        }

        private void numericPositionYFont1_ValueChanged(object sender, EventArgs e)
        {
            _positionYFont1 = (int)numericPositionYFont1.Value;
            applyFont();
        }

        private void selectColorFont1Btn_Click(object sender, EventArgs e)
        {
            ColorDialog MyDialog = new ColorDialog();
            MyDialog.AllowFullOpen = true;
            MyDialog.ShowHelp = true;
            MyDialog.Color = textBoxColorFont1.BackColor;
            if (MyDialog.ShowDialog() == DialogResult.OK)
            {
                textBoxColorFont1.BackColor = MyDialog.Color;
                _colorFont1 = MyDialog.Color.ToArgb();
                applyFont();
            }
        }

        private void radioButtonLeft1_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonLeft1.Checked)
                return;

            _aligmentFont1 = "Left";
            applyFont();
        }

        private void radioButtonCenter1_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonCenter1.Checked)
                return;

            _aligmentFont1 = "Center";
            applyFont();
        }

        private void radioButtonRight1_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonRight1.Checked)
                return;

            _aligmentFont1 = "Right";
            applyFont();
        }

        private void descriptionTwoTxt_TextChanged(object sender, EventArgs e)
        {
            applyFont();
        }

        private void numericFontSize2_ValueChanged(object sender, EventArgs e)
        {
            _fontSize2 = (float)numericFontSize2.Value;
            applyFont();
        }

        private void numericPositionXFont2_ValueChanged(object sender, EventArgs e)
        {
            _positionXFont2 = (int)numericPositionXFont2.Value;
            applyFont();
        }

        private void numericPositionYFont2_ValueChanged(object sender, EventArgs e)
        {
            _positionYFont2 = (int)numericPositionYFont2.Value;
            applyFont();
        }

        private void selectColorFont2Btn_Click(object sender, EventArgs e)
        {
            ColorDialog MyDialog = new ColorDialog();
            MyDialog.AllowFullOpen = true;
            MyDialog.ShowHelp = true;
            MyDialog.Color = textBoxColorFont2.BackColor;
            if (MyDialog.ShowDialog() == DialogResult.OK)
            {
                textBoxColorFont2.BackColor = MyDialog.Color;
                _colorFont2 = MyDialog.Color.ToArgb();
                applyFont();
            }
        }

        private void radioButtonLeft2_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonLeft2.Checked)
                return;

            _aligmentFont2 = "Left";
            applyFont();
        }

        private void radioButtonCenter2_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonCenter2.Checked)
                return;

            _aligmentFont2 = "Left";
            applyFont();
        }

        private void radioButtonRight2_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonRight2.Checked)
                return;

            _aligmentFont2 = "Left";
            applyFont();
        }



        private void applyFont()
        {
            if (_combinedConfigBitmap == null)
                return;

            textoPictureBox.Image = (Bitmap)_combinedConfigBitmap.Clone();
            Graphics g = Graphics.FromImage(textoPictureBox.Image);

            try
            {
                if (descriptionOneTxt.Text != "")
                {
                    Font font1 = new Font(_fontFamilyName, _fontSize1, FontStyle.Bold, GraphicsUnit.Pixel);
                    Color color = Color.FromArgb(_colorFont1);
                    SolidBrush brush = new SolidBrush(color);
                    StringFormat sf = new StringFormat();

                    if (radioButtonCenter1.Checked)
                        sf.Alignment = StringAlignment.Center;
                    else if (radioButtonRight1.Checked)
                        sf.Alignment = StringAlignment.Far;
                    else
                        sf.Alignment = StringAlignment.Near;

                    Rectangle atpoint1 = new Rectangle(new Point(_positionXFont1, _positionYFont1), new Size(_combinedConfigBitmap.Width, (int)_fontSize1 + 20));
                    g.DrawString(descriptionOneTxt.Text, font1, brush, atpoint1, sf);
                }

                if (descriptionTwoTxt.Text != "")
                {
                    Font font2 = new Font(_fontFamilyName, _fontSize2, FontStyle.Bold, GraphicsUnit.Pixel);
                    Color color2 = Color.FromArgb(_colorFont2);
                    SolidBrush brush2 = new SolidBrush(color2);
                    StringFormat sf2 = new StringFormat();

                    if (radioButtonCenter2.Checked)
                        sf2.Alignment = StringAlignment.Center;
                    else if (radioButtonRight2.Checked)
                        sf2.Alignment = StringAlignment.Far;
                    else
                        sf2.Alignment = StringAlignment.Near;

                    Rectangle atpoint2 = new Rectangle(new Point(_positionXFont2, _positionYFont2), new Size(_combinedConfigBitmap.Width, (int)_fontSize2 + 20));
                    g.DrawString(descriptionTwoTxt.Text, font2, brush2, atpoint2, sf2);
                }

            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

        }

        private void savePictureBtn_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog save = new SaveFileDialog();
                save.Filter = "Jpg(*Jpg)|*.jpg";
                save.AddExtension = true;
                if (save.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    positionPictureBox.Image.Save(save.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                save.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed saving image");
            }
        }

        private void saveBackgroundConfigBtn_Click(object sender, EventArgs e)
        {
            IDTextBox.Text = "";
            numericPosXBackground.Value = _picX;
            numericPosYBackground.Value = _picY;
            numericScaleBackground.Value = (decimal)_picScale;
            effectBackground.Text = (radioButtonNone.Checked) ? "None" : (radioButtonGrayscale.Checked) ? "Grayscale" : (radioButtonSepia.Checked) ? "Sepia" : "None";
            saveBackgroundIDBtn.Enabled = false;
            configurationControl.SelectedTab = backgroundTab;

        }

        private void IDTextBox_TextChanged(object sender, EventArgs e)
        {
            if (IDTextBox.Text != "")
                saveBackgroundIDBtn.Enabled = true;
            else
                saveBackgroundIDBtn.Enabled = false;
        }

        private void saveBackgroundIDBtn_Click(object sender, EventArgs e)
        {
            bool exist = false;
            string[] details = new string[] { IDTextBox.Text, numericPosXBackground.Value.ToString(), numericPosYBackground.Value.ToString(), numericScaleBackground.Value.ToString(), effectBackground.Text };
            ListViewItem itemToSave = new ListViewItem(details);

            foreach (ListViewItem itemSaved in backgroundsList.Items)
            {
                if (itemSaved.SubItems[0].Text == IDTextBox.Text)
                {
                    exist = true;

                    var confirmResult = MessageBox.Show("this ID exists, do you want to replace it ?", "Replace ID", MessageBoxButtons.YesNo);
                    if (confirmResult == DialogResult.Yes)
                    {
                        // if 'Yes' do something here 
                        itemSaved.SubItems[1].Text = details[1];
                        itemSaved.SubItems[2].Text = details[2];
                        itemSaved.SubItems[3].Text = details[3];
                    }
                    else
                    {
                        // if 'No' do something here 

                    }

                }

            }

            if (!exist)
            {
                backgroundsList.Items.Add(itemToSave);
            }

            IDTextBox.Text = "";

        }

        private void removeItemBtn_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in backgroundsList.SelectedItems)
            {
                backgroundsList.Items.Remove(item);
            }
        }

        private void savePictureWithTextBtn_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog save = new SaveFileDialog();
                save.Filter = "Jpg(*Jpg)|*.jpg";
                save.AddExtension = true;
                if (save.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    textoPictureBox.Image.Save(save.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
                save.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed saving image");
            }
        }

        private void backgroundsList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (backgroundsList.SelectedItems.Count > 0)
            {
                removeItemBtn.Enabled = true;
            }
            else
            {
                removeItemBtn.Enabled = false;
            }

        }

        private void browseFolderBtn_Click(object sender, EventArgs e)
        {

            try
            {
                FolderBrowserDialog browse = new FolderBrowserDialog();
                if (browse.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    outputFolderText.Text = browse.SelectedPath;
                }
                browse.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed browsing folder");
            }

        }

        private void browseSourceBtn_Click(object sender, EventArgs e)
        {
            try
            {
                FolderBrowserDialog browse = new FolderBrowserDialog();
                if (browse.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    sourceFolderText.Text = browse.SelectedPath;
                }
                browse.Dispose();
            }
            catch (Exception)
            {
                throw new ApplicationException("Failed browsing folder");
            }
        }

        //CAMERA

        private void StartUpCamera()
        {
            this.SafeCall(() =>
            {
                cameraCollectionComboBox.SelectedIndexChanged += this.HandleCameraSelectionChanged;
                takePictureBtn.Enabled = false;
                _manager.LoadFramework();
            }, ex =>
            {
                MessageBox.Show(ex.ToString(), Resources.FrameworkLoadError, MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            });
        }

        private void HandleCameraSelectionChanged(object sender, EventArgs e)
        {
            this.UpdateCameraControls();
        }

        private void HandleCameraAdded(object sender, EventArgs e)
        {
            this.LoadCameras();
        }

        private void SafeCall(Action action, Action<Exception> exceptionHandler)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                this.Invoke(new ThisDelegate(a =>
                {
                    debugLabel.Text = a;
                }), ex.Message);
                /* 
                 if (this.InvokeRequired) this.Invoke(exceptionHandler, ex);
                 else exceptionHandler(ex);*/
            }
        }

        private void LoadCameras()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(this.LoadCameras));
            }
            else
            {
                cameraCollectionComboBox.SelectedIndex = -1;
                cameraCollectionComboBox.Items.Clear();
                imageFormatTextInput.Text = "";
                imageSizeTextInput.Text = "";
                imageCompressTextInput.Text = "";
                foreach (var camera in _manager.GetCameras())
                {
                    camera.Shutdown += this.HandleCameraShutdown;
                    camera.PictureTaken += this.HandlePictureUpdate;

                    cameraCollectionComboBox.Items.Add(camera);
                }
                if (cameraCollectionComboBox.Items.Count > 0)
                    cameraCollectionComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateCameraControls()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(this.UpdateCameraControls));
                return;
            }

            var camera = this.GetSelectedCamera();
            if (camera == null)
            {
                takePictureBtn.Enabled = false;
            }
            else
            {
                try
                {
                    if (camera.IsInHostLiveViewMode)
                    {
                        camera.StopLiveView();
                    }

                    imageFormatTextInput.Text = camera.ImageQuality.PrimaryImageFormat.ToString();
                    imageSizeTextInput.Text = camera.ImageQuality.PrimaryImageSize.ToString();
                    imageCompressTextInput.Text = camera.ImageQuality.PrimaryCompressLevel.ToString();

                    takePictureBtn.Enabled = true;
                }
                catch (EosException)
                {
                    takePictureBtn.Enabled = false;
                }
            }
        }

        private void HandleCameraShutdown(object sender, EventArgs e)
        {
            this.LoadCameras();
            this.UpdateCameraControls();
        }

        private void HandlePictureUpdate(object sender, EosImageEventArgs e)
        {
            this.UpdatePicture(e.GetImage());
        }

        private void takePictureBtn_Click(object sender, EventArgs e)
        {
            TakePhotoTCP(true);
        }

        private EosCamera GetSelectedCamera()
        {

            return (EosCamera)this.Invoke(new Func<EosCamera>(() =>
            {
                return cameraCollectionComboBox.Items.Count > 0 && cameraCollectionComboBox.SelectedIndex >= 0
                ? cameraCollectionComboBox.SelectedItem as EosCamera : null;
            }));
        }

        private void UpdatePicture(Image image)
        {
            if (this.InvokeRequired)
                this.Invoke(new Action(() => this.UpdatePicture(image)));
            else
            {
                _cropUserBitmap = new Bitmap(image);
                userCropPictureBox.Image = (Bitmap)_cropUserBitmap.Clone();
            }
        }

        //CONFIG

        private void saveConfigBtn_Click(object sender, EventArgs e)
        {
            //Crop
            _settings.AppSettings.Settings["CropUserX"].Value = _cropUserX.ToString();
            _settings.AppSettings.Settings["CropUserY"].Value = _cropUserY.ToString();
            _settings.AppSettings.Settings["CropUserWidth"].Value = _cropUserWidth.ToString();
            _settings.AppSettings.Settings["CropUserHeight"].Value = _cropUserHeight.ToString();

            //Chroma
            _settings.AppSettings.Settings["ChromaHue"].Value = _hue.ToString();
            _settings.AppSettings.Settings["ChromaTolerance"].Value = _tolerance.ToString();
            _settings.AppSettings.Settings["ChromaSaturation"].Value = _saturation.ToString();
            _settings.AppSettings.Settings["ChromaMinValue"].Value = _minValue.ToString();
            _settings.AppSettings.Settings["ChromaMaxValue"].Value = _maxValue.ToString();

            //Fonts
            _settings.AppSettings.Settings["FontFamilyName"].Value = _fontFamilyName;
            _settings.AppSettings.Settings["FontSize1"].Value = _fontSize1.ToString();
            _settings.AppSettings.Settings["PositionXFont1"].Value = _positionXFont1.ToString();
            _settings.AppSettings.Settings["PositionYFont1"].Value = _positionYFont1.ToString();
            _settings.AppSettings.Settings["ColorFont1"].Value = _colorFont1.ToString();
            _settings.AppSettings.Settings["AligmentFont1"].Value = _aligmentFont1;
            _settings.AppSettings.Settings["FontSize2"].Value = _fontSize2.ToString();
            _settings.AppSettings.Settings["PositionXFont2"].Value = _positionXFont2.ToString();
            _settings.AppSettings.Settings["PositionYFont2"].Value = _positionYFont2.ToString();
            _settings.AppSettings.Settings["ColorFont2"].Value = _colorFont2.ToString();
            _settings.AppSettings.Settings["AligmentFont2"].Value = _aligmentFont2;

            //Position
            _settings.AppSettings.Settings["PhotoWidth"].Value = _photoWidth.ToString();
            _settings.AppSettings.Settings["PhotoHeight"].Value = _photoHeight.ToString();
            _settings.AppSettings.Settings["PicPositionX"].Value = _picX.ToString();
            _settings.AppSettings.Settings["PicPositionY"].Value = _picY.ToString();
            _settings.AppSettings.Settings["PicScale"].Value = _picScale.ToString();

            //Background

            string searchPattern1 = "PicPositionX_";
            string searchPattern2 = "PicPositionY_";
            string searchPattern3 = "PicScale_";
            string searchPattern4 = "PicEffect_";

            foreach (string key in _settings.AppSettings.Settings.AllKeys)
            {
                if (key.IndexOf(searchPattern1) == 0 || key.IndexOf(searchPattern2) == 0 || key.IndexOf(searchPattern3) == 0 || key.IndexOf(searchPattern4) == 0)
                {
                    _settings.AppSettings.Settings.Remove(key);
                }
            }

            foreach (ListViewItem itemToSave in backgroundsList.Items)
            {

                _settings.AppSettings.Settings.Add("PicPositionX_" + itemToSave.SubItems[0].Text, itemToSave.SubItems[1].Text);
                _settings.AppSettings.Settings.Add("PicPositionY_" + itemToSave.SubItems[0].Text, itemToSave.SubItems[2].Text);
                _settings.AppSettings.Settings.Add("PicScale_" + itemToSave.SubItems[0].Text, itemToSave.SubItems[3].Text);
                _settings.AppSettings.Settings.Add("PicEffect_" + itemToSave.SubItems[0].Text, itemToSave.SubItems[4].Text);
            }

            //General
            _settings.AppSettings.Settings["CameraSourcePath"].Value = sourceFolderText.Text;
            _settings.AppSettings.Settings["PhotoDestPath"].Value = outputFolderText.Text;

            //Change watcher place
            if (_settings.AppSettings.Settings["CameraSourcePath"].Value != "")
                CAMERA_DUMP_PATH = _settings.AppSettings.Settings["CameraSourcePath"].Value;

            watcher.Path = CAMERA_DUMP_PATH;

            _settings.Save();
        }





    }
}
