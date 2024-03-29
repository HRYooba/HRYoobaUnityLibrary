﻿// In-game Debug Console https://assetstore.unity.com/packages/tools/gui/in-game-debug-console-68068?aid=1101lGoY&cid=1101l7tzhnwF&utm_source=aff カスタムしてます
/*
The MIT License (MIT)

Copyright (c) 2016 Süleyman Yasir KULA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/


using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.IO;

// Receives debug entries and custom events (e.g. Clear, Collapse, Filter by Type)
// and notifies the recycled list view of changes to the list of debug entries
// 
// - Vocabulary -
// Debug/Log entry: a Debug.Log/LogError/LogWarning/LogException/LogAssertion request made by
//                   the client and intercepted by this manager object
// Debug/Log item: a visual (uGUI) representation of a debug entry
// 
// There can be a lot of debug entries in the system but there will only be a handful of log items 
// to show their properties on screen (these log items are recycled as the list is scrolled)

// An enum to represent filtered log types
namespace HRYooba.UI
{
    public enum DebugLogFilter
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 4,
        All = 7
    }

    public class DebugLogConsoleWindow : MonoBehaviour
    {
#pragma warning disable 0649
        // Debug console will persist between scenes
        [Header("Properties")]

        // Minimum height of the console window
        [SerializeField]
        [HideInInspector]
        private float minimumHeight = 200f;

        [SerializeField]
        [HideInInspector]
        private string logcatArguments;

        [Header("Visuals")]
        [SerializeField]
        private DebugLogItem logItemPrefab;

        // Visuals for different log types
        [SerializeField]
        private Sprite infoLog;
        [SerializeField]
        private Sprite warningLog;
        [SerializeField]
        private Sprite errorLog;

        private Dictionary<LogType, Sprite> logSpriteRepresentations;

        [SerializeField]
        private Color collapseButtonNormalColor;
        [SerializeField]
        private Color collapseButtonSelectedColor;

        [SerializeField]
        private Color filterButtonsNormalColor;
        [SerializeField]
        private Color filterButtonsSelectedColor;

        [Header("Internal References")]
        [SerializeField]
        private RectTransform logWindowTR;

        private RectTransform canvasTR;

        [SerializeField]
        private RectTransform logItemsContainer;

        [SerializeField]
        private Image collapseButton;

        [SerializeField]
        private Image filterInfoButton;
        [SerializeField]
        private Image filterWarningButton;
        [SerializeField]
        private Image filterErrorButton;

        [SerializeField]
        private Text infoEntryCountText;
        [SerializeField]
        private Text warningEntryCountText;
        [SerializeField]
        private Text errorEntryCountText;

        [SerializeField]
        private GameObject snapToBottomButton;

        // Canvas group to modify visibility of the log window
        [SerializeField]
        private CanvasGroup logWindowCanvasGroup;

        [SerializeField]
        private ScrollRect logItemsScrollRect;

        // Recycled list view to handle the log items efficiently
        [SerializeField]
        private DebugLogRecycledListView recycledListView;
#pragma warning restore 0649

        // Number of entries filtered by their types
        private int infoEntryCount = 0, warningEntryCount = 0, errorEntryCount = 0;

        private bool isLogWindowVisible = true;
        private bool screenDimensionsChanged = false;

        // Filters to apply to the list of debug entries to show
        private bool isCollapseOn = false;
        private DebugLogFilter logFilter = DebugLogFilter.All;

        // If the last log item is completely visible (scrollbar is at the bottom),
        // scrollbar will remain at the bottom when new debug entries are received
        private bool snapToBottom = true;

        // List of unique debug entries (duplicates of entries are not kept)
        private List<DebugLogEntry> collapsedLogEntries;

        // Dictionary to quickly find if a log already exists in collapsedLogEntries
        private Dictionary<DebugLogEntry, int> collapsedLogEntriesMap;

        // The order the collapsedLogEntries are received 
        // (duplicate entries have the same index (value))
        private DebugLogIndexList uncollapsedLogEntriesIndices;

        // Filtered list of debug entries to show
        private DebugLogIndexList indicesOfListEntriesToShow;

        // Logs that should be registered in Update-loop
        private List<QueuedDebugLogEntry> queuedLogs;

        private List<DebugLogItem> pooledLogItems;

        // Required in ValidateScrollPosition() function
        private PointerEventData nullPointerEventData;

        private void Awake()
        {
            pooledLogItems = new List<DebugLogItem>();
            queuedLogs = new List<QueuedDebugLogEntry>();

            canvasTR = (RectTransform)transform;

            // Associate sprites with log types
            logSpriteRepresentations = new Dictionary<LogType, Sprite>
                {
                    { LogType.Log, infoLog },
                    { LogType.Warning, warningLog },
                    { LogType.Error, errorLog },
                    { LogType.Exception, errorLog },
                    { LogType.Assert, errorLog }
                };

            // Initially, all log types are visible
            filterInfoButton.color = filterButtonsSelectedColor;
            filterWarningButton.color = filterButtonsSelectedColor;
            filterErrorButton.color = filterButtonsSelectedColor;

            collapsedLogEntries = new List<DebugLogEntry>(128);
            collapsedLogEntriesMap = new Dictionary<DebugLogEntry, int>(128);
            uncollapsedLogEntriesIndices = new DebugLogIndexList();
            indicesOfListEntriesToShow = new DebugLogIndexList();

            recycledListView.Initialize(this, collapsedLogEntries, indicesOfListEntriesToShow, logItemPrefab.Transform.sizeDelta.y);
            recycledListView.UpdateItemsInTheList(true);

            if (minimumHeight < 200f)
                minimumHeight = 200f;

            nullPointerEventData = new PointerEventData(null);
        }

        private void OnEnable()
        {
            // Intercept debug entries
            Application.logMessageReceived -= ReceivedLog;
            Application.logMessageReceived += ReceivedLog;

            //Debug.LogAssertion( "assert" );
            //Debug.LogError( "error" );
            //Debug.LogException( new System.IO.EndOfStreamException() );
            //Debug.LogWarning( "warning" );
            //Debug.Log( "log" );
        }

        private void OnDisable()
        {
            // Stop receiving debug entries
            Application.logMessageReceived -= ReceivedLog;
        }

        // Launch in popup mode
        private void Start()
        {
            ShowLogWindow();
        }

        // Window is resized, update the list
        private void OnRectTransformDimensionsChange()
        {
            screenDimensionsChanged = true;
        }

        // If snapToBottom is enabled, force the scrollbar to the bottom
        private void LateUpdate()
        {
            int queuedLogCount = queuedLogs.Count;
            if (queuedLogCount > 0)
            {
                for (int i = 0; i < queuedLogCount; i++)
                {
                    QueuedDebugLogEntry logEntry = queuedLogs[i];
                    ReceivedLog(logEntry.logString, logEntry.stackTrace, logEntry.logType);
                }

                queuedLogs.Clear();
            }

            if (screenDimensionsChanged)
            {
                // Update the recycled list view
                if (isLogWindowVisible)
                    recycledListView.OnViewportDimensionsChanged();

                screenDimensionsChanged = false;
            }

            if (snapToBottom)
            {
                logItemsScrollRect.verticalNormalizedPosition = 0f;

                if (snapToBottomButton.activeSelf)
                    snapToBottomButton.SetActive(false);
            }
            else
            {
                float scrollPos = logItemsScrollRect.verticalNormalizedPosition;
                if (snapToBottomButton.activeSelf != (scrollPos > 1E-6f && scrollPos < 0.9999f))
                    snapToBottomButton.SetActive(!snapToBottomButton.activeSelf);
            }
        }

        public void ShowLogWindow()
        {
            // Show the log window
            // logWindowCanvasGroup.interactable = true;
            // logWindowCanvasGroup.blocksRaycasts = true;
            // logWindowCanvasGroup.alpha = 1f;

            // Update the recycled list view 
            // (in case new entries were intercepted while log window was hidden)
            recycledListView.OnLogEntriesUpdated(true);

            isLogWindowVisible = true;
        }

        // A debug entry is received
        private void ReceivedLog(string logString, string stackTrace, LogType logType)
        {
            if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
            {
                // Trying to update the UI while the canvas is being rebuilt will throw warnings in the Unity console
                queuedLogs.Add(new QueuedDebugLogEntry(logString, stackTrace, logType));
                return;
            }

            DebugLogEntry logEntry = new DebugLogEntry(System.DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss] ") + logString, stackTrace, null);

            // Check if this entry is a duplicate (i.e. has been received before)
            int logEntryIndex;
            bool isEntryInCollapsedEntryList = collapsedLogEntriesMap.TryGetValue(logEntry, out logEntryIndex);
            if (!isEntryInCollapsedEntryList)
            {
                // It is not a duplicate,
                // add it to the list of unique debug entries
                logEntry.logTypeSpriteRepresentation = logSpriteRepresentations[logType];

                logEntryIndex = collapsedLogEntries.Count;
                collapsedLogEntries.Add(logEntry);
                collapsedLogEntriesMap[logEntry] = logEntryIndex;
            }
            else
            {
                // It is a duplicate,
                // increment the original debug item's collapsed count
                logEntry = collapsedLogEntries[logEntryIndex];
                logEntry.count++;
            }

            // Add the index of the unique debug entry to the list
            // that stores the order the debug entries are received
            uncollapsedLogEntriesIndices.Add(logEntryIndex);

            // If this debug entry matches the current filters,
            // add it to the list of debug entries to show
            Sprite logTypeSpriteRepresentation = logEntry.logTypeSpriteRepresentation;
            if (isCollapseOn && isEntryInCollapsedEntryList)
            {
                if (isLogWindowVisible)
                    recycledListView.OnCollapsedLogEntryAtIndexUpdated(logEntryIndex);
            }
            else if (logFilter == DebugLogFilter.All ||
               (logTypeSpriteRepresentation == infoLog && ((logFilter & DebugLogFilter.Info) == DebugLogFilter.Info)) ||
               (logTypeSpriteRepresentation == warningLog && ((logFilter & DebugLogFilter.Warning) == DebugLogFilter.Warning)) ||
               (logTypeSpriteRepresentation == errorLog && ((logFilter & DebugLogFilter.Error) == DebugLogFilter.Error)))
            {
                indicesOfListEntriesToShow.Add(logEntryIndex);

                if (isLogWindowVisible)
                    recycledListView.OnLogEntriesUpdated(false);
            }

            if (logType == LogType.Log)
            {
                infoEntryCount++;
                infoEntryCountText.text = infoEntryCount.ToString();
            }
            else if (logType == LogType.Warning)
            {
                warningEntryCount++;
                warningEntryCountText.text = warningEntryCount.ToString();
            }
            else
            {
                errorEntryCount++;
                errorEntryCountText.text = errorEntryCount.ToString();
            }
        }

        // Value of snapToBottom is changed (user scrolled the list manually)
        public void SetSnapToBottom(bool snapToBottom)
        {
            this.snapToBottom = snapToBottom;
        }

        // Make sure the scroll bar of the scroll rect is adjusted properly
        public void ValidateScrollPosition()
        {
            logItemsScrollRect.OnScroll(nullPointerEventData);
        }

        // Clear button is clicked
        public void ClearButtonPressed()
        {
            snapToBottom = true;

            infoEntryCount = 0;
            warningEntryCount = 0;
            errorEntryCount = 0;

            infoEntryCountText.text = "0";
            warningEntryCountText.text = "0";
            errorEntryCountText.text = "0";

            collapsedLogEntries.Clear();
            collapsedLogEntriesMap.Clear();
            uncollapsedLogEntriesIndices.Clear();
            indicesOfListEntriesToShow.Clear();

            recycledListView.DeselectSelectedLogItem();
            recycledListView.OnLogEntriesUpdated(true);
        }

        // Collapse button is clicked
        public void CollapseButtonPressed()
        {
            // Swap the value of collapse mode
            isCollapseOn = !isCollapseOn;

            snapToBottom = true;
            collapseButton.color = isCollapseOn ? collapseButtonSelectedColor : collapseButtonNormalColor;
            recycledListView.SetCollapseMode(isCollapseOn);

            // Determine the new list of debug entries to show
            FilterLogs();
        }

        // Filtering mode of info logs has been changed
        public void FilterLogButtonPressed()
        {
            logFilter = logFilter ^ DebugLogFilter.Info;

            if ((logFilter & DebugLogFilter.Info) == DebugLogFilter.Info)
                filterInfoButton.color = filterButtonsSelectedColor;
            else
                filterInfoButton.color = filterButtonsNormalColor;

            FilterLogs();
        }

        // Filtering mode of warning logs has been changed
        public void FilterWarningButtonPressed()
        {
            logFilter = logFilter ^ DebugLogFilter.Warning;

            if ((logFilter & DebugLogFilter.Warning) == DebugLogFilter.Warning)
                filterWarningButton.color = filterButtonsSelectedColor;
            else
                filterWarningButton.color = filterButtonsNormalColor;

            FilterLogs();
        }

        // Filtering mode of error logs has been changed
        public void FilterErrorButtonPressed()
        {
            logFilter = logFilter ^ DebugLogFilter.Error;

            if ((logFilter & DebugLogFilter.Error) == DebugLogFilter.Error)
                filterErrorButton.color = filterButtonsSelectedColor;
            else
                filterErrorButton.color = filterButtonsNormalColor;

            FilterLogs();
        }

        // Debug window is being resized,
        // Set the sizeDelta property of the window accordingly while
        // preventing window dimensions from going below the minimum dimensions
        public void Resize(BaseEventData dat)
        {
            PointerEventData eventData = (PointerEventData)dat;

            // Grab the resize button from top; 36f is the height of the resize button
            float newHeight = (eventData.position.y - logWindowTR.position.y) / -canvasTR.localScale.y + 36f;
            if (newHeight < minimumHeight)
                newHeight = minimumHeight;

            Vector2 anchorMin = logWindowTR.anchorMin;
            anchorMin.y = Mathf.Max(0f, 1f - newHeight / canvasTR.sizeDelta.y);
            logWindowTR.anchorMin = anchorMin;

            // Update the recycled list view
            recycledListView.OnViewportDimensionsChanged();
        }

        // Determine the filtered list of debug entries to show on screen
        private void FilterLogs()
        {
            if (logFilter == DebugLogFilter.None)
            {
                // Show no entry
                indicesOfListEntriesToShow.Clear();
            }
            else if (logFilter == DebugLogFilter.All)
            {
                if (isCollapseOn)
                {
                    // All the unique debug entries will be listed just once.
                    // So, list of debug entries to show is the same as the
                    // order these unique debug entries are added to collapsedLogEntries
                    indicesOfListEntriesToShow.Clear();
                    for (int i = 0; i < collapsedLogEntries.Count; i++)
                        indicesOfListEntriesToShow.Add(i);
                }
                else
                {
                    indicesOfListEntriesToShow.Clear();
                    for (int i = 0; i < uncollapsedLogEntriesIndices.Count; i++)
                        indicesOfListEntriesToShow.Add(uncollapsedLogEntriesIndices[i]);
                }
            }
            else
            {
                // Show only the debug entries that match the current filter
                bool isInfoEnabled = (logFilter & DebugLogFilter.Info) == DebugLogFilter.Info;
                bool isWarningEnabled = (logFilter & DebugLogFilter.Warning) == DebugLogFilter.Warning;
                bool isErrorEnabled = (logFilter & DebugLogFilter.Error) == DebugLogFilter.Error;

                if (isCollapseOn)
                {
                    indicesOfListEntriesToShow.Clear();
                    for (int i = 0; i < collapsedLogEntries.Count; i++)
                    {
                        DebugLogEntry logEntry = collapsedLogEntries[i];
                        if (logEntry.logTypeSpriteRepresentation == infoLog && isInfoEnabled)
                            indicesOfListEntriesToShow.Add(i);
                        else if (logEntry.logTypeSpriteRepresentation == warningLog && isWarningEnabled)
                            indicesOfListEntriesToShow.Add(i);
                        else if (logEntry.logTypeSpriteRepresentation == errorLog && isErrorEnabled)
                            indicesOfListEntriesToShow.Add(i);
                    }
                }
                else
                {
                    indicesOfListEntriesToShow.Clear();
                    for (int i = 0; i < uncollapsedLogEntriesIndices.Count; i++)
                    {
                        DebugLogEntry logEntry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];
                        if (logEntry.logTypeSpriteRepresentation == infoLog && isInfoEnabled)
                            indicesOfListEntriesToShow.Add(uncollapsedLogEntriesIndices[i]);
                        else if (logEntry.logTypeSpriteRepresentation == warningLog && isWarningEnabled)
                            indicesOfListEntriesToShow.Add(uncollapsedLogEntriesIndices[i]);
                        else if (logEntry.logTypeSpriteRepresentation == errorLog && isErrorEnabled)
                            indicesOfListEntriesToShow.Add(uncollapsedLogEntriesIndices[i]);
                    }
                }
            }

            // Update the recycled list view
            recycledListView.DeselectSelectedLogItem();
            recycledListView.OnLogEntriesUpdated(true);

            ValidateScrollPosition();
        }

        public string GetAllLogs()
        {
            int count = uncollapsedLogEntriesIndices.Count;
            int length = 0;
            int newLineLength = System.Environment.NewLine.Length;
            for (int i = 0; i < count; i++)
            {
                DebugLogEntry entry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];
                length += entry.logString.Length + entry.stackTrace.Length + newLineLength * 3;
            }

            length += 100; // Just in case...

            System.Text.StringBuilder sb = new System.Text.StringBuilder(length);
            for (int i = 0; i < count; i++)
            {
                DebugLogEntry entry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];
                sb.AppendLine(entry.logString).AppendLine(entry.stackTrace).AppendLine();
            }

            return sb.ToString();
        }

        private void SaveLogsToFile()
        {
            string path = Path.Combine(Application.persistentDataPath, System.DateTime.Now.ToString("dd-MM-yyyy--HH-mm-ss") + ".txt");
            File.WriteAllText(path, GetAllLogs());

            Debug.Log("Logs saved to: " + path);
        }

        // Pool an unused log item
        public void PoolLogItem(DebugLogItem logItem)
        {
            logItem.gameObject.SetActive(false);
            pooledLogItems.Add(logItem);
        }

        // Fetch a log item from the pool
        public DebugLogItem PopLogItem()
        {
            DebugLogItem newLogItem;

            // If pool is not empty, fetch a log item from the pool,
            // create a new log item otherwise
            if (pooledLogItems.Count > 0)
            {
                newLogItem = pooledLogItems[pooledLogItems.Count - 1];
                pooledLogItems.RemoveAt(pooledLogItems.Count - 1);
                newLogItem.gameObject.SetActive(true);
            }
            else
            {
                newLogItem = (DebugLogItem)Instantiate(logItemPrefab, logItemsContainer, false);
                newLogItem.Initialize(recycledListView);
            }

            return newLogItem;
        }
    }
}