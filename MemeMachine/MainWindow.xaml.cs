using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NAudio.Wave;
using Gma.System.MouseKeyHook;
using Microsoft.Win32;
using MemeMachine.Properties;
using System.Threading;
using GameOverlay.Windows;
using GameOverlay.Drawing;
using System.Threading.Tasks;
using System.Drawing;

namespace MemeMachine
{
    public partial class MainWindow : MetroWindow
    {
        private KeyboardSimulator keyboardSimulator = new KeyboardSimulator();

        public ObservableCollection<MemeSound> memeList = new ObservableCollection<MemeSound>();
        private List<string> addedFileList = new List<string>();

        // DirectSoundOut
        private DirectSoundOut outputDevice;
        private DirectSoundOut defaultDevice;
        private AudioFileReader audioFileForOutput;
        private AudioFileReader audioFileForDefault;
        private Guid selectedDevice;
        private DateTime soundPlayStart;
        private DateTime soundPlayEnd;

        // GlobalMouseHook
        private IKeyboardMouseEvents globalMouseHook;
        private IKeyboardEvents globalKeyboardHook;

        // Rebindables
        private int pttKey;
        private int navigationKey;
        private bool navigationKeyDown;
        private int mouseButton;

        // Overlay Variables
        private OverlayWindow osdWindow;
        private GameOverlay.Drawing.Graphics osdGraphics;
        private static List<string> fontNames = new List<string>();
        private static GameOverlay.Drawing.Font osdFont;
        private static GameOverlay.Drawing.SolidBrush brushTransparent;
        private static GameOverlay.Drawing.SolidBrush brushNormalText;
        private static GameOverlay.Drawing.SolidBrush brushSelectedText;
        private static GameOverlay.Drawing.SolidBrush brushPlayingText;
        private static GameOverlay.Drawing.SolidBrush brushBackground;
        private static GameOverlay.Drawing.SolidBrush brushProgressOutline;
        private static GameOverlay.Drawing.SolidBrush brushProgressFill;


        private bool hasLoaded = false;

        private int rangeStart = 0;
        private int rangeSize;

        public MainWindow()
        {
            InitializeComponent();
            UpdateStatusBarText("Initializing...");
            #region Populate DataGrids & ComboBoxes
            cbDeviceList.ItemsSource = DirectSoundOut.Devices;
            dgMemeFiles.ItemsSource = memeList;
            cbKeyCodes.ItemsSource = Enum.GetValues(typeof(KeyboardSimulator.ScanCodeShort));
            cbNavigateKeyCode.ItemsSource = Enum.GetNames(typeof(System.Windows.Forms.Keys));
            cbMouseButton.ItemsSource = Enum.GetNames(typeof(System.Windows.Forms.MouseButtons));
            #endregion
            #region Look for Last Used Device
            Guid previousDevice = GetPreviousOutputDevice();
            if(previousDevice != Guid.Empty)
            {
                for(int i=0; i < DirectSoundOut.Devices.Count(); i++)
                {
                    if(DirectSoundOut.Devices.ToArray()[i].Guid == previousDevice)
                    {
                        cbDeviceList.SelectedItem = cbDeviceList.Items.GetItemAt(i);
                        lSelectedDevice.Content = $"Selected Device: {previousDevice.ToString()}";
                        break;
                    }
                }
            }
            #endregion
            #region Look for Last Used PTT Key
            if(Properties.Settings.Default.LastPTTKey != 0)
            {
                int lastPTTKey = Properties.Settings.Default.LastPTTKey;

                foreach(KeyboardSimulator.ScanCodeShort scanCode in Enum.GetValues(typeof(KeyboardSimulator.ScanCodeShort)))
                {
                    if((int)scanCode == lastPTTKey)
                    {
                        cbKeyCodes.SelectedItem = scanCode;
                        break;
                    }
                }
            }
            #endregion
            #region Look for the Last Navigation Key
            if (!String.IsNullOrWhiteSpace(Properties.Settings.Default.LastNavigateKey))
            {
                string lastNavigateKey = Properties.Settings.Default.LastNavigateKey;
                foreach(String key in Enum.GetNames(typeof(System.Windows.Forms.Keys)))
                {
                    if(key == lastNavigateKey)
                    {
                        cbNavigateKeyCode.SelectedItem = key;
                        navigationKey = (int)Enum.Parse(typeof(System.Windows.Forms.Keys), key);
                        break;
                    }
                }
            }
            #endregion
            #region Look for the Last Mouse Button
            if (!String.IsNullOrWhiteSpace(Properties.Settings.Default.LastMouseButton))
            {
                string lastMouseButton = Properties.Settings.Default.LastMouseButton;
                foreach (String button in Enum.GetNames(typeof(System.Windows.Forms.MouseButtons)))
                {
                    if (button == lastMouseButton)
                    {
                        cbMouseButton.SelectedItem = button;
                        mouseButton = (int)Enum.Parse(typeof(System.Windows.Forms.MouseButtons), button);
                        break;
                    }
                }
            }
            #endregion
            
            #region OSD CheckBoxes
            cbEnableOSD.IsChecked = Settings.Default.bOSDEnabled;
            cbHideOSD.IsChecked = Settings.Default.bOSDHide;
            cbOSDProgress.IsChecked = Settings.Default.bOSDProgress;
            cbOSDPlaying.IsChecked = Settings.Default.bOSDPlaying;
            #endregion
            

            // OSD Stuff WIP
            GetSystemFonts();
            //SetupOSDWindow();
            //RenderOSD();

            // TODO: Move most of this shit to window loaded event?
        }

        public void GetSystemFonts()
        {
            foreach (System.Drawing.FontFamily font in System.Drawing.FontFamily.Families)
            {
                fontNames.Add(font.Name);
            }
            cbFonts.ItemsSource = fontNames;
            cbFonts.SelectedItem = cbFonts.Items[0];
        }

        public void SetupOSDWindow()
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            Console.WriteLine($"Screen Resolution: {screenWidth}x{screenHeight}");

            osdWindow = new OverlayWindow(0, 0, screenWidth, screenHeight)
            {
                IsTopmost = true,
                IsVisible = true
            };
            osdGraphics = new GameOverlay.Drawing.Graphics
            {
                MeasureFPS = false,
                Height = osdWindow.Height,
                Width = osdWindow.Width,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true,
                UseMultiThreadedFactories = false,
                VSync = true,
                WindowHandle = IntPtr.Zero
            };

            osdWindow.CreateWindow();
            osdGraphics.WindowHandle = osdWindow.Handle;
            osdGraphics.Setup();

            //testFont = osdGraphics.CreateFont(osdFont, 16f);
        }

        private void UpdateStatusBarText(string newText)
        {
            tStatusText.Text = newText;
        }

        private void SubscribeHooks()
        {
            globalMouseHook = Hook.GlobalEvents();
            globalKeyboardHook = Hook.GlobalEvents();

            globalMouseHook.MouseWheelExt += GlobalHookMouseWheel;
            globalMouseHook.MouseDownExt += GlobalHookMouseDown;

            globalKeyboardHook.KeyDown += GlobalHookKeyDown;
            globalKeyboardHook.KeyUp += GlobalHookKeyUp;
        }

        private void UnsubscribeHooks()
        {
            globalMouseHook.MouseWheelExt -= GlobalHookMouseWheel;
            globalMouseHook.MouseDownExt -= GlobalHookMouseDown;

            globalMouseHook.Dispose();
        }

        private void GlobalHookKeyDown(object sender, System.Windows.Forms.KeyEventArgs args)
        {
            if(args.KeyCode == (System.Windows.Forms.Keys)navigationKey)
            {
                navigationKeyDown = true;
                args.Handled = true;
            }
        }

        private void GlobalHookKeyUp(object sender, System.Windows.Forms.KeyEventArgs args)
        {
            if (args.KeyCode == (System.Windows.Forms.Keys)navigationKey)
            {
                navigationKeyDown = false;
                args.Handled = true;
            }
        }

        private void GlobalHookMouseWheel(object sender, MouseEventExtArgs args)
        {
            int listCount = dgMemeFiles.Items.Count;

            if(listCount > 0)
            {
                int currentIndex = dgMemeFiles.SelectedIndex;
                if(args.Delta == 120 && navigationKeyDown)
                {
                    if (currentIndex > 0)
                    {
                        dgMemeFiles.SelectedItem = dgMemeFiles.Items.GetItemAt(currentIndex-1);
                        args.Handled = true;
                    } else {
                        dgMemeFiles.SelectedItem = dgMemeFiles.Items.GetItemAt(0);
                        args.Handled = true;
                    }
                }
                if (args.Delta == -120 && navigationKeyDown)
                {
                    if (currentIndex < (listCount-1))
                    {
                        dgMemeFiles.SelectedItem = dgMemeFiles.Items.GetItemAt(currentIndex + 1);
                        args.Handled = true;
                    } else {
                        dgMemeFiles.SelectedItem = dgMemeFiles.Items.GetItemAt(listCount-1);
                        args.Handled = true;
                    }
                }
                foreach(MemeSound memeSound in memeList)
                {
                    memeSound.isSelected = false;
                }
                if(dgMemeFiles.SelectedIndex >= 0)
                {
                    memeList[dgMemeFiles.SelectedIndex].isSelected = true;
                }
                //args.Handled = true;
            }
        }
        private void GlobalHookMouseDown(object sender, MouseEventExtArgs args)
        {
            if(args.Button == (System.Windows.Forms.MouseButtons)mouseButton && navigationKeyDown && selectedDevice == Guid.Empty)
            {
                UpdateStatusBarText("ERROR! Unable to Play - No Output Device Has Been Selected.");
                return;
            }

            MemeSound selectedSound = dgMemeFiles.SelectedItem as MemeSound;

            if(selectedDevice != Guid.Empty)
            {
                // Make Sure a Sound is Actually Selected First
                if (selectedSound != null)
                {
                    if (args.Button == (System.Windows.Forms.MouseButtons)mouseButton && navigationKeyDown)
                    {
                        if (audioFileForOutput != null)
                        {
                            outputDevice.Stop();
                            if (audioFileForDefault != null)
                            {
                                defaultDevice.Stop();
                                return;
                            }
                            return;
                        }
                    }

                    if (args.Button == (System.Windows.Forms.MouseButtons)mouseButton && navigationKeyDown)
                    {
                        if (outputDevice == null)
                        {
                            outputDevice = new DirectSoundOut(selectedDevice);
                            outputDevice.PlaybackStopped += OnOutputPlaybackStopped;
                            if (cbPlayOnDefault.IsChecked.Value && defaultDevice == null)
                            {
                                defaultDevice = new DirectSoundOut(DirectSoundOut.DSDEVID_DefaultPlayback);
                                defaultDevice.PlaybackStopped += OnDefaultPlaybackStopped;
                            }
                        }

                        if (audioFileForOutput == null)
                        {
                            try
                            {
                                audioFileForOutput = new AudioFileReader(selectedSound.Path);
                                audioFileForOutput.Volume = (float)sOutputVolume.Value / 100.0f;
                                outputDevice.Init(audioFileForOutput);
                                if (cbPlayOnDefault.IsChecked.Value && audioFileForDefault == null)
                                {
                                    audioFileForDefault = new AudioFileReader(selectedSound.Path);
                                    audioFileForDefault.Volume = (float)sDefaultVolume.Value / 100.0f;
                                    defaultDevice.Init(audioFileForDefault);
                                }
                            } catch (Exception ex) {
                                if(MessageBox.Show($"An error occurred when trying to play {selectedSound.Path}. It has probably been renamed, moved or deleted!\n\nError Message:\n{ex.Message}\n\nDo you wish to delete this file from the list?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
                                {
                                    addedFileList.Remove(selectedSound.Path);
                                    memeList.Remove(selectedSound);
                                    Settings.Default.LastFileList = String.Join(";", addedFileList.ToArray());
                                    Settings.Default.Save();
                                    return;
                                }
                                return;
                            }
                        }

                        // Play the Sounds
                        outputDevice.Play();
                        defaultDevice.Play();
                        // Indicate Which Song is Being Played
                        selectedSound.isPlaying = true;
                        UpdateStatusBarText($"Playing {selectedSound.Name}.");
                        // Used for Progress Bar. Probably inaccurate and ineffiecent. Sadly outputDevice.PlaybackPosition causes a TON of lag if used in a while loop.
                        soundPlayStart = DateTime.Now;
                        soundPlayEnd = DateTime.Now.AddTicks(selectedSound.Length.Ticks);
                        // Simulate PTT
                        if(cbSimulatePTT.IsChecked.Value)
                        {
                            keyboardSimulator.Send((KeyboardSimulator.ScanCodeShort)pttKey);
                        }
                    }
                }
            }
        }
        private void OnOutputPlaybackStopped(object sender, StoppedEventArgs args)
        {
            foreach(MemeSound memeSound in memeList)
            {
                memeSound.isPlaying = false;
            }
            if(outputDevice != null)
            {
                outputDevice.Dispose();
                outputDevice = null;
                audioFileForOutput.Dispose();
                audioFileForOutput = null;
            }
            // If We're Simulating a KeyPress, Release It
            if(cbSimulatePTT.IsChecked.Value)
            {
                keyboardSimulator.Release((KeyboardSimulator.ScanCodeShort)pttKey);
            }
            UpdateStatusBarText("Ready...");
        }
        private void OnDefaultPlaybackStopped(object sender, StoppedEventArgs args)
        {
            if(defaultDevice != null)
            {
                defaultDevice.Dispose();
                defaultDevice = null;
                audioFileForDefault.Dispose();
                audioFileForDefault = null;
            }
            UpdateStatusBarText("Ready...");
        }
        private Guid GetPreviousOutputDevice()
        {
            if(Settings.Default.LastOutputDevice != Guid.Empty)
            {
                return Settings.Default.LastOutputDevice;
            } else {
                return Guid.Empty;
            }
        }
        private void BAddFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Sound Files(*.wav;*.mp3)|*.wav;*.mp3|WAVE Files (*.wav)|*.wav|MP3 Files (*.mp3)|*.mp3";
            ofd.Multiselect = true;
            ofd.Title = "Select Sound File(s)...";

            if (ofd.ShowDialog() == true)
            {
                foreach(String file in ofd.FileNames)
                {
                    try
                    {
                        AudioFileReader afr = new AudioFileReader(file);
                        string memeName = System.IO.Path.GetFileNameWithoutExtension(file);
                        string memePath = file;
                        TimeSpan memeLength = afr.TotalTime;
                        memeList.Add(new MemeSound { Name = memeName, Length = memeLength, Path = memePath });
                        afr.Dispose();
                        addedFileList.Add(memePath); // TODO: Avoid Duplicates
                        Properties.Settings.Default.LastFileList = String.Join(";", addedFileList.ToArray());
                        Properties.Settings.Default.Save();
                    } catch (Exception ex) {
                        MessageBox.Show($"An error has occured while trying to add {file}. Error Message:\n\n{ex.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetupOSDWindow();
            #region Load Last Used Audio Files
            // TODO: Replace, store added files in LocalAppData. Remove file if removed from list.
            if (!String.IsNullOrWhiteSpace(Properties.Settings.Default.LastFileList))
            {
                string[] storedFiles = Properties.Settings.Default.LastFileList.Split(';');

                foreach (string filePath in storedFiles)
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        AudioFileReader afr = new AudioFileReader(filePath);
                        string memeName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                        string memePath = filePath;
                        TimeSpan memeLength = afr.TotalTime;
                        memeList.Add(new MemeSound { Name = memeName, Length = memeLength, Path = memePath });
                        afr.Dispose();
                        addedFileList.Add(filePath);
                    } else {
                        MessageBox.Show($"Failed to locate {filePath}.\n\nHas it been renamed, moved or deleted?\n\nThis file will be ignored to prevent issues.", "Warning", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
                Properties.Settings.Default.LastFileList = String.Join(";", addedFileList.ToArray());
                Properties.Settings.Default.Save();
            }
            #endregion
            #region Last OSD Colours
            string[] storedColours = Settings.Default.sStoredColours.Split(';');
            cpNormalColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[0]);
            cpSelectedColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[1]);
            cpPlayingColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[2]);
            cpBackgroundColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[3]);
            cpProgressOutlineColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[4]);
            cpProgressBarFillColour.SelectedColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(storedColours[5]);
            UpdateOSDBrushes();
            #endregion
            #region Look for the Last Volume Values
            if (Convert.ToInt32(sOutputVolume.Value) != Settings.Default.LastVolumeOutput)
            {
                sOutputVolume.Value = Convert.ToDouble(Settings.Default.LastVolumeOutput);
            }
            if (Convert.ToInt32(sDefaultVolume.Value) != Settings.Default.LastVolumeDefault)
            {
                sDefaultVolume.Value = Convert.ToDouble(Settings.Default.LastVolumeDefault);
            }
            #endregion
            nudFontSize.Value = Settings.Default.dFontSize;
            nudListSize.Value = Settings.Default.dListSize;
            cbFonts.SelectedItem = Settings.Default.sFont;
            rangeSize = (int)Settings.Default.dListSize;
            UpdateOSDFont();
            SubscribeHooks();
            hasLoaded = true;
            RenderOSD();
            UpdateStatusBarText("Ready...");
        }
        private void UpdateOSDBrushes()
        {
            if(osdGraphics != null)
            {
                brushTransparent = osdGraphics.CreateSolidBrush(0.0f, 0.0f, 0.0f, 0.0f);
                brushNormalText = osdGraphics.CreateSolidBrush(cpNormalColour.SelectedColor.Value.R, cpNormalColour.SelectedColor.Value.G, cpNormalColour.SelectedColor.Value.B, cpNormalColour.SelectedColor.Value.A);
                brushSelectedText = osdGraphics.CreateSolidBrush(cpSelectedColour.SelectedColor.Value.R, cpSelectedColour.SelectedColor.Value.G, cpSelectedColour.SelectedColor.Value.B, cpSelectedColour.SelectedColor.Value.A);
                brushPlayingText = osdGraphics.CreateSolidBrush(cpPlayingColour.SelectedColor.Value.R, cpPlayingColour.SelectedColor.Value.G, cpPlayingColour.SelectedColor.Value.B, cpPlayingColour.SelectedColor.Value.A);
                brushBackground = osdGraphics.CreateSolidBrush(cpBackgroundColour.SelectedColor.Value.R, cpBackgroundColour.SelectedColor.Value.G, cpBackgroundColour.SelectedColor.Value.B, cpBackgroundColour.SelectedColor.Value.A);
                brushProgressOutline = osdGraphics.CreateSolidBrush(cpProgressOutlineColour.SelectedColor.Value.R, cpProgressOutlineColour.SelectedColor.Value.G, cpProgressOutlineColour.SelectedColor.Value.B, cpProgressOutlineColour.SelectedColor.Value.A);
                brushProgressFill = osdGraphics.CreateSolidBrush(cpProgressBarFillColour.SelectedColor.Value.R, cpProgressBarFillColour.SelectedColor.Value.G, cpProgressBarFillColour.SelectedColor.Value.B, cpProgressBarFillColour.SelectedColor.Value.A);
            }
        }
        private void UpdateOSDFont()
        {
            if(osdGraphics != null)
            {
                osdFont = osdGraphics.CreateFont(cbFonts.SelectedItem.ToString(), (float)nudFontSize.Value.GetValueOrDefault());
            }
        }
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.LastFileList = String.Join(";", addedFileList.ToArray());
            Properties.Settings.Default.LastVolumeOutput = (int)sOutputVolume.Value;
            Properties.Settings.Default.LastVolumeDefault = (int)sDefaultVolume.Value;
            Properties.Settings.Default.Save();

            UnsubscribeHooks();
        }
        private void BRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if(dgMemeFiles.SelectedItems.Count > 0)
            {
                List<MemeSound> memesToDelete = new List<MemeSound>();
                foreach(MemeSound memeSound in dgMemeFiles.SelectedItems)
                {
                    addedFileList.Remove(memeSound.Path);
                    memesToDelete.Add(memeSound);
                }
                foreach(MemeSound memeSound in memesToDelete)
                {
                    memeList.Remove(memeSound);
                }
                Properties.Settings.Default.LastFileList = String.Join(";", addedFileList.ToArray());
                Properties.Settings.Default.Save();
            }
        }
        private void DgMemeFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(e.ChangedButton == MouseButton.Left)
            {
                if(selectedDevice != Guid.Empty)
                {
                    MemeSound selectedSound = dgMemeFiles.SelectedItem as MemeSound;

                    if(selectedSound != null)
                    {
                        if(defaultDevice == null && audioFileForDefault == null)
                        {
                            defaultDevice = new DirectSoundOut(DirectSoundOut.DSDEVID_DefaultPlayback);
                            defaultDevice.PlaybackStopped += OnDefaultPlaybackStopped;
                            audioFileForDefault = new AudioFileReader(selectedSound.Path);
                            audioFileForDefault.Volume = (float)sDefaultVolume.Value / 100.0f;
                            defaultDevice.Init(audioFileForDefault);
                            defaultDevice.Play();
                            UpdateStatusBarText($"Previewing: {selectedSound.Name} (Not Being Transmitted)");
                        } else {
                            defaultDevice.Stop();
                        }
                    }
                }
            }
        }
        private async void RenderOSD()
        {
            while(true)
            {
                osdGraphics.BeginScene();
                osdGraphics.ClearScene(brushTransparent);
                if (cbEnableOSD.IsChecked.Value && navigationKeyDown || !cbHideOSD.IsChecked.Value || cbOSDPlaying.IsChecked.Value && outputDevice != null)
                {
                    if(dgMemeFiles.SelectedIndex >= 0)
                    {
                        int counter = 0;
                        osdGraphics.FillRoundedRectangle(brushBackground, 0f, 0f, (float)Math.Pow(osdFont.FontSize, 2), (osdFont.FontSize * Math.Min(rangeSize+1, (dgMemeFiles.Items.Count - rangeStart))+5f), 3.0f);
                        List<MemeSound> subList = memeList.ToList().GetRange(rangeStart, Math.Min(rangeSize+1, (dgMemeFiles.Items.Count-rangeStart)));
                        foreach (MemeSound memeSound in subList)
                        {
                            if(memeSound.isPlaying)
                            {
                                if(cbOSDProgress.IsChecked.GetValueOrDefault())
                                {
                                    float songPercentage = (float)((DateTime.Now - soundPlayStart).TotalSeconds * 100 / (soundPlayEnd - soundPlayStart).TotalSeconds);
                                    osdGraphics.DrawVerticalProgressBar(brushProgressOutline, brushProgressFill, 1, (counter * osdFont.FontSize), (float)Math.Pow(osdFont.FontSize, 2), (counter * osdFont.FontSize)+osdFont.FontSize+5f, 2.0f, songPercentage);
                                }
                                osdGraphics.DrawTextWithLayout(osdFont, osdFont.FontSize, brushPlayingText, brushTransparent, 10, counter * osdFont.FontSize, new GameOverlay.Drawing.Rectangle(10, counter * osdFont.FontSize, (float)Math.Pow(osdFont.FontSize, 2), (counter * osdFont.FontSize)+osdFont.FontSize+5f), memeSound.Name, 2);
                            } else {
                                osdGraphics.DrawTextWithLayout(osdFont, osdFont.FontSize, (memeSound.isSelected) ? brushSelectedText : brushNormalText, brushTransparent, 10, counter * osdFont.FontSize, new GameOverlay.Drawing.Rectangle(10, counter * osdFont.FontSize, (float)Math.Pow(osdFont.FontSize, 2), (counter * osdFont.FontSize)+osdFont.FontSize+5f), memeSound.Name, 2);
                            }
                            counter++;
                        }
                    }
                }
                osdGraphics.EndScene();
                await Task.Delay(10);
            }
        }

        #region ComboBox Events TODO: Merge Into One Handler?
        private void CbKeyCodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbKeyCodes.SelectedItem != null)
            {
                int selectedScanCode = (int)(KeyboardSimulator.ScanCodeShort)cbKeyCodes.SelectedItem;
                pttKey = selectedScanCode;
                Properties.Settings.Default.LastPTTKey = selectedScanCode;
                Properties.Settings.Default.Save();
            }
        }
        private void CbNavigateKeyCode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbKeyCodes.SelectedItem != null)
            {
                string keyName = (string)cbNavigateKeyCode.SelectedItem;
                if (Settings.Default.LastNavigateKey != keyName)
                {
                    navigationKey = (int)Enum.Parse(typeof(System.Windows.Forms.Keys), keyName);
                    Settings.Default.LastNavigateKey = keyName;
                    Settings.Default.Save();
                }
            }
        }
        private void CbMouseButton_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbMouseButton.SelectedItem != null)
            {
                string buttonName = (string)cbMouseButton.SelectedItem;
                if (Properties.Settings.Default.LastMouseButton != buttonName)
                {
                    mouseButton = (int)Enum.Parse(typeof(System.Windows.Forms.MouseButtons), buttonName);
                    Properties.Settings.Default.LastMouseButton = buttonName;
                    Properties.Settings.Default.Save();
                }
            }
        }
        private void CbDeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DirectSoundDeviceInfo selectedItem = cbDeviceList.SelectedItem as DirectSoundDeviceInfo;
            if (selectedItem.Guid != selectedDevice)
            {
                selectedDevice = selectedItem.Guid;
                lSelectedDevice.Content = $"Selected Device: {selectedDevice.ToString()}";
                Properties.Settings.Default.LastOutputDevice = selectedDevice;
                Properties.Settings.Default.Save();
            }
        }
        private void CbFonts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(hasLoaded)
            {
                if(cbFonts.SelectedIndex != -1)
                {
                    UpdateOSDFont();
                    Settings.Default.sFont = cbFonts.SelectedItem.ToString();
                    Settings.Default.Save();
                }
            }
        }
        #endregion
        #region CheckBox Handlers
        private void CheckBoxHandler_OSD(object sender, RoutedEventArgs args)
        {
            CheckBox theCheckBox = sender as CheckBox;
            if (theCheckBox == cbEnableOSD)
            {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bOSDEnabled = true : Settings.Default.bOSDEnabled = false;
            } else if(theCheckBox == cbHideOSD) {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bOSDHide = true : Settings.Default.bOSDHide = false;
            } else if(theCheckBox == cbOSDProgress) {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bOSDProgress = true : Settings.Default.bOSDProgress = false;
            } else if(theCheckBox == cbOSDPlaying) {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bOSDPlaying = true : Settings.Default.bOSDPlaying = false;
            }
            Settings.Default.Save();
        }

        private void CheckBoxHandler_Settings(object sender, RoutedEventArgs args)
        {
            CheckBox theCheckBox = sender as CheckBox;
            if(theCheckBox == cbPlayOnDefault)
            {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bPlayOnDefault = true : Settings.Default.bPlayOnDefault = false;
            } else if (theCheckBox == cbSimulatePTT) {
                _ = (theCheckBox.IsChecked.Value) ? Settings.Default.bSimulatePTT = true : Settings.Default.bSimulatePTT = false;
            } else if (theCheckBox == cbNavigateKeybind) {
                // Not Used ATM; There Must Always be a Navigate Keybind
            } else if (theCheckBox == cbStartButton) {
                // Not Used ATM; There Must Always be a Start/Stop Keybind
            }
        }
        #endregion
        #region ColourPicker Handler
        private void ColourPickerHandler_OSD(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            if(hasLoaded)
            {
                List<ColorPickerLib.Controls.ColorPicker> cpList = new List<ColorPickerLib.Controls.ColorPicker>();

                cpList.Add(cpNormalColour);
                cpList.Add(cpSelectedColour);
                cpList.Add(cpPlayingColour);
                cpList.Add(cpBackgroundColour);
                cpList.Add(cpProgressOutlineColour);
                cpList.Add(cpProgressBarFillColour);

                StringBuilder colourString = new StringBuilder();

                foreach (ColorPickerLib.Controls.ColorPicker cp in cpList)
                {
                    try
                    {
                        colourString.Append("#" + cp.SelectedColorText + ";");
                    } catch (Exception ex) {
                        // TODO: Causes an error when app is started if not caught. SelectedColorChanged probably gets triggered before the SelectedColorText is set on init?
                    }
                }
                UpdateOSDBrushes();
                Settings.Default.sStoredColours = colourString.ToString();
                Settings.Default.Save();
            }
        }
        #endregion
        #region Volume Slider Events
        private void SDefaultVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (defaultDevice != null && audioFileForDefault != null)
            {
                audioFileForDefault.Volume = (float)sDefaultVolume.Value / 100.0f;
            }
            Settings.Default.LastVolumeDefault = (int)sDefaultVolume.Value;
            Settings.Default.Save();
        }
        private void SOutputVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (outputDevice != null && audioFileForOutput != null)
            {
                audioFileForOutput.Volume = (float)sOutputVolume.Value / 100.0f;
            }
            Settings.Default.LastVolumeOutput = (int)sOutputVolume.Value;
            Settings.Default.Save();
        }
        #endregion
        #region Right-Click Events (mostly for copying stuff to clipboard)
        private void LSelectedDevice_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Clipboard.SetText(lSelectedDevice.Content.ToString().Replace("Selected Device: ", ""));
        }
        #endregion

        private void NudFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            if(hasLoaded)
            {
                if(nudFontSize.Value != Settings.Default.dFontSize)
                {
                    UpdateOSDFont();
                    Settings.Default.dFontSize = nudFontSize.Value.GetValueOrDefault();
                    Settings.Default.Save();
                }
            }
        }

        private void NudListSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            if (hasLoaded)
            {
                if (nudListSize.Value != Settings.Default.dListSize)
                {
                    rangeSize = (int)nudListSize.Value.GetValueOrDefault();
                    Settings.Default.dListSize = nudListSize.Value.GetValueOrDefault();
                    Settings.Default.Save();
                }
            }
        }

        private void DgMemeFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            rangeStart = Math.Min(dgMemeFiles.SelectedIndex, (dgMemeFiles.Items.Count - rangeStart));
            //rangeEnd = Math.Min(4, (dgMemeFiles.Items.Count - rangeStart));
        }
    }
}
