using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PSDExtractor
{
    public class PsdExtractorWindow : EditorWindow
    {

        string  _psdPath        = "";
        string  _outputFolder   = "Assets/PSD_Exports";
        bool    _skipHidden     = true;
        bool    _skipGroups     = true;
        bool    _autoImport     = true;

        bool    _isProcessing   = false;
        string  _statusMessage  = "";
        MessageType _statusType = MessageType.None;

        List<LayerPreviewItem> _previewItems = new();
        Vector2 _scrollPos;
        
        Rect _dropRect;
        

        [MenuItem("Tools/PSD Extractor")]
        public static void Open()
        {
            var win = GetWindow<PsdExtractorWindow>("PSD Extractor");
            win.minSize = new Vector2(420, 560);
            win.Show();
        }
        

        void OnGUI()
        {
            DrawHeader();
            GUILayout.Space(8);

            DrawDropZone();
            GUILayout.Space(8);

            DrawOptions();
            GUILayout.Space(8);

            DrawExtractButton();
            GUILayout.Space(6);

            DrawStatus();

            if (_previewItems.Count > 0)
            {
                GUILayout.Space(4);
                DrawPreview();
            }

            HandleDragAndDrop();
        }
        

        void DrawHeader()
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 16,
                alignment = TextAnchor.MiddleCenter,
            };
            headerStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("⬛  PSD Layer Extractor", headerStyle, GUILayout.Height(28));

            var subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10,
            };
            EditorGUILayout.LabelField("Extract PSD layers as individual PNG textures", subtitleStyle);
            EditorGUILayout.Space(4);

          
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.35f, 0.35f, 0.35f));
        }
        

        void DrawDropZone()
        {
            bool hasPsd = !string.IsNullOrEmpty(_psdPath) && File.Exists(_psdPath);

          
            Color borderColor = hasPsd
                ? new Color(0.35f, 0.75f, 0.40f)
                : new Color(0.45f, 0.45f, 0.55f);

            Color bgColor = hasPsd
                ? new Color(0.18f, 0.28f, 0.19f)
                : new Color(0.20f, 0.20f, 0.25f);

            
            _dropRect = EditorGUILayout.GetControlRect(false, 80);
            _dropRect = new Rect(_dropRect.x + 4, _dropRect.y, _dropRect.width - 8, _dropRect.height);

            EditorGUI.DrawRect(_dropRect, bgColor);

           
            float bw = 1.5f;
            EditorGUI.DrawRect(new Rect(_dropRect.x, _dropRect.y, _dropRect.width, bw), borderColor);
            EditorGUI.DrawRect(new Rect(_dropRect.x, _dropRect.yMax - bw, _dropRect.width, bw), borderColor);
            EditorGUI.DrawRect(new Rect(_dropRect.x, _dropRect.y, bw, _dropRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(_dropRect.xMax - bw, _dropRect.y, bw, _dropRect.height), borderColor);
            
            var dropStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = true,
            };

            if (hasPsd)
            {
                dropStyle.normal.textColor = new Color(0.55f, 0.95f, 0.60f);
                dropStyle.fontStyle = FontStyle.Bold;
                string filename = Path.GetFileName(_psdPath);
                EditorGUI.LabelField(_dropRect, $"✔  {filename}", dropStyle);
            }
            else
            {
                dropStyle.normal.textColor = new Color(0.60f, 0.60f, 0.70f);
                EditorGUI.LabelField(_dropRect, "Drop .psd file here\nor click Browse", dropStyle);
            }
            
            Rect browseRect = new Rect(_dropRect.xMax - 76, _dropRect.yMax - 24, 72, 20);
            if (GUI.Button(browseRect, "Browse…", EditorStyles.miniButton))
            {
                string picked = EditorUtility.OpenFilePanel("Select PSD file", "", "psd");
                if (!string.IsNullOrEmpty(picked))
                    LoadPsd(picked);
            }
            
            if (hasPsd)
            {
                Rect clearRect = new Rect(_dropRect.x + 4, _dropRect.yMax - 24, 52, 20);
                if (GUI.Button(clearRect, "Clear", EditorStyles.miniButton))
                {
                    _psdPath = "";
                    _previewItems.Clear();
                    _statusMessage = "";
                }
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                {
                    _outputFolder = EditorGUILayout.TextField("Folder", _outputFolder);
                    if (GUILayout.Button("…", GUILayout.Width(26)))
                    {
                        string abs = Application.dataPath.Replace("/Assets", "");
                        string picked = EditorUtility.OpenFolderPanel("Output folder", abs, "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            
                            if (picked.StartsWith(abs))
                                _outputFolder = picked.Substring(abs.Length + 1);
                            else
                                _outputFolder = picked;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
                _skipHidden = EditorGUILayout.Toggle("Skip hidden layers", _skipHidden);
                _skipGroups = EditorGUILayout.Toggle("Skip group layers", _skipGroups);

                GUILayout.Space(4);
                EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
                _autoImport = EditorGUILayout.Toggle("Auto-import into AssetDatabase", _autoImport);
            }
            EditorGUILayout.EndVertical();
        }
        

        void DrawExtractButton()
        {
            bool canExtract = !string.IsNullOrEmpty(_psdPath) && File.Exists(_psdPath) && !_isProcessing;

            GUI.enabled = canExtract;

            var btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize    = 13,
                fontStyle   = FontStyle.Bold,
                fixedHeight = 38,
            };

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = canExtract ? new Color(0.25f, 0.65f, 0.30f) : Color.gray;

            if (GUILayout.Button("⬛  Extract Layers", btnStyle))
                RunExtraction();

            GUI.backgroundColor = prev;
            GUI.enabled = true;
        }
        

        void DrawStatus()
        {
            if (string.IsNullOrEmpty(_statusMessage)) return;
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }
        

        void DrawPreview()
        {
            EditorGUILayout.LabelField($"Layers found: {_previewItems.Count}", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(160));
            {
                foreach (var item in _previewItems)
                {
                    Color c = item.IsGroup ? new Color(0.7f, 0.7f, 0.4f)
                            : item.IsHidden ? new Color(0.5f, 0.5f, 0.5f)
                            : new Color(0.8f, 0.9f, 0.8f);

                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = c;

                    string icon = item.IsGroup ? "▶" : item.IsHidden ? "○" : "●";
                    string size = (item.W > 0 && item.H > 0) ? $"  [{item.W}×{item.H}]" : "";
                    EditorGUILayout.LabelField($"  {icon}  {item.Name}{size}", style);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        

        void HandleDragAndDrop()
        {
            Event e = Event.current;
            if (e == null) return;

            bool overZone = _dropRect.Contains(e.mousePosition);

            if (!overZone) return;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDrop.paths.Length == 1 &&
                    DragAndDrop.paths[0].EndsWith(".psd", StringComparison.OrdinalIgnoreCase)
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (DragAndDrop.paths.Length > 0)
                {
                    string p = DragAndDrop.paths[0];
                    if (p.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
                        LoadPsd(p);
                    else
                        SetStatus("Only .psd files are supported.", MessageType.Warning);
                }
                e.Use();
            }
        }
        

        void LoadPsd(string path)
        {
            _psdPath       = path;
            _previewItems.Clear();
            _statusMessage = "";

            try
            {
                var doc = PsdReader.Read(path);
                foreach (var layer in doc.Layers)
                {
                    _previewItems.Add(new LayerPreviewItem
                    {
                        Name     = layer.Name,
                        IsGroup  = layer.IsGroup,
                        IsHidden = !layer.IsVisible,
                        W        = layer.LayerWidth,
                        H        = layer.LayerHeight,
                    });
                }
                SetStatus($"Loaded Path.GetFileName(path) — {doc.Layers.Count} layer(s) found.", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to read PSD: {ex.Message}", MessageType.Error);
                _psdPath = "";
            }

            Repaint();
        }
        

        void RunExtraction()
        {
            if (string.IsNullOrEmpty(_psdPath) || !File.Exists(_psdPath)) return;

            _isProcessing  = true;
            _statusMessage = "";
            _statusType    = MessageType.None;

            try
            {
                var doc = PsdReader.Read(_psdPath);
                
                string projectRoot = Application.dataPath.Replace("/Assets", "");
                string absOut = Path.IsPathRooted(_outputFolder)
                    ? _outputFolder
                    : Path.Combine(projectRoot, _outputFolder);

                string psdBaseName = Path.GetFileNameWithoutExtension(_psdPath);
                string layerDir    = Path.Combine(absOut, psdBaseName);
                Directory.CreateDirectory(layerDir);

                int exported = 0;

                foreach (var layer in doc.Layers)
                {
                    if (_skipGroups && layer.IsGroup)   continue;
                    if (_skipHidden && !layer.IsVisible) continue;
                    if (layer.Pixels == null || layer.Pixels.Length == 0) continue;
                    if (layer.LayerWidth <= 0 || layer.LayerHeight <= 0) continue;

                    string safeName = MakeSafeFilename(layer.Name);
                    if (string.IsNullOrEmpty(safeName)) safeName = $"layer_{exported}";

                    // Avoid duplicate filenames
                    string filePath = Path.Combine(layerDir, safeName + ".png");
                    int dup = 1;
                    while (File.Exists(filePath))
                    {
                        filePath = Path.Combine(layerDir, $"{safeName}_{dup}.png");
                        dup++;
                    }

                    var tex = new Texture2D(layer.LayerWidth, layer.LayerHeight, TextureFormat.RGBA32, false);

                    // Flip vertically: PSD is top-to-bottom, Unity texture is bottom-to-top
                    Color32[] flipped = FlipVertical(layer.Pixels, layer.LayerWidth, layer.LayerHeight);
                    tex.SetPixels32(flipped);
                    tex.Apply();

                    byte[] png = tex.EncodeToPNG();
                    File.WriteAllBytes(filePath, png);
                    DestroyImmediate(tex);

                    exported++;
                    EditorUtility.DisplayProgressBar(
                        "PSD Extractor",
                        $"Exporting: {layer.Name}",
                        (float)exported / doc.Layers.Count);
                }

                EditorUtility.ClearProgressBar();

                if (_autoImport) AssetDatabase.Refresh();

                SetStatus(
                    $"Done! Exported {exported} layer(s) to:\n{layerDir}",
                    MessageType.Info);
                
                if (_autoImport)
                {
                    string relPath = MakeRelativePath(projectRoot, layerDir);
                    var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relPath);
                    if (folder != null) EditorGUIUtility.PingObject(folder);
                    else Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relPath);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                SetStatus($"Extraction failed: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                _isProcessing = false;
                Repaint();
            }
        }

        void SetStatus(string msg, MessageType type)
        {
            _statusMessage = msg;
            _statusType    = type;
        }

        static Color32[] FlipVertical(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * w;
                int dstRow = (h - 1 - y) * w;
                Array.Copy(src, srcRow, dst, dstRow, w);
            }
            return dst;
        }

        static string MakeSafeFilename(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "layer";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim().Replace(' ', '_');
        }

        static string MakeRelativePath(string projectRoot, string absPath)
        {
            if (absPath.StartsWith(projectRoot + Path.DirectorySeparatorChar))
                return absPath.Substring(projectRoot.Length + 1)
                              .Replace('\\', '/');
            return absPath;
        }
        
        class LayerPreviewItem
        {
            public string Name;
            public bool   IsGroup;
            public bool   IsHidden;
            public int    W, H;
        }
    }
}
