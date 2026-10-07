/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * ConfigsPanel can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using EnigmaVirtual.Notifications;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class ConfigsPanel : PanelBase
    {
        [SerializeField]
        private GameObject verticalLayoutGroupObj = null;
        [SerializeField]
        private Button closeButton = null;
        [SerializeField]
        private Button saveButton = null;
        [SerializeField]
        private Button resetPlayerPrefsButton;
        [SerializeField]
        private Toggle overwriteJsonToggle = null;
        [SerializeField]
        private Toggle overwritePrefsToggle = null;

        private SettingInput[] _fields;

        protected override void Awake()
        {
            overwriteJsonToggle.isOn = false;
            overwritePrefsToggle.isOn = false;

#if UNITY_ANDROID || UNITY_IOS
            overwriteJsonToggle.gameObject.SetActive(false);
            overwritePrefsToggle.isOn = true;
#else
            // The reset button stays visible on every platform now. It used to be hidden here on
            // the reasoning that desktop does not use PlayerPrefs - but a blob can exist anyway, from an
            // older build or a branched project's own code, and Core.Awake prefers it over settings.json
            // and gates the file read out entirely. Hiding the only way to clear it meant every edit to
            // settings.json was silently ignored with no route back from inside the app.
            overwritePrefsToggle.gameObject.SetActive(false);
            overwriteJsonToggle.isOn = true;
#endif

            _fields = GetComponentsInChildren<SettingInput>(true);
            base.Awake();
        }

        private void Update()
        {
            if (isVisible)
            {
                if (overwriteJsonToggle.isOn || overwritePrefsToggle.isOn)
                    saveButton.interactable = true;
                else
                    saveButton.interactable = false;
            }
        }

        public override void Show(bool animate = true, Action callback = null)
        {
            verticalLayoutGroupObj.gameObject.SetActive(true);
            resetPlayerPrefsButton.onClick.AddListener(OnResetPLayerPrefsClicked);
            saveButton.onClick.AddListener(OnSaveButtonClicked);
            closeButton.onClick.AddListener(OnCloseButtonClicked);
            base.Show(animate, callback);
        }

        public override void Hide(bool animate = true, Action callback = null)
        {
            verticalLayoutGroupObj.gameObject.SetActive(false);
            resetPlayerPrefsButton.onClick.RemoveListener(OnResetPLayerPrefsClicked);
            saveButton.onClick.RemoveListener(OnSaveButtonClicked);
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
            base.Hide(animate, callback);
        }

        private void OnResetPLayerPrefsClicked()
        {
            // Guarded like every other call in this class - Core logs and continues when the manager is
            // absent, so a branched project without NotificationsPanel reaches here with it null.
            if (PlayerPrefs.HasKey("Settings"))
            {
                PlayerPrefs.DeleteKey("Settings");
                PlayerPrefs.Save();

                // "Restart" is not politeness. Core._isUsingPlayerPrefs was latched at boot and nothing
                // clears it, so settings.json stays ignored for the rest of this session - and this button
                // is the remedy the boot error tells the operator to use, so it has to say what it takes.
                if (Core.notificationManager != null)
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success,
                        "STORED SETTINGS CLEARED", "Restart the app for settings.json to be read again.");
                }
            }
            else if (Core.notificationManager != null)
            {
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Info,
                    "NOTHING TO CLEAR", "This machine has no stored settings; settings.json is already "
                    + "what gets read.");
            }
        }

        private void OnCloseButtonClicked()
        {
            Hide();
        }

        void OnSaveButtonClicked()
        {
            // Reset before the loop so the count belongs to this save and not to a previous one.
            SettingsParse.ResetErrorCount();

            // Every field is applied first, then the file is written **once**. This loop used to pass the
            // toggle into Save(), which wrote the whole of Core.settings to disk per field - so a panel
            // with a dozen inputs rewrote settings.json a dozen times, and the earlier writes captured a
            // half-applied settings object.
            for (int i = 0; i < _fields.Length; i++)
            {
                // Inactive fields are skipped deliberately and silently. _fields is collected with
                // GetComponentsInChildren<SettingInput>(true), so a field on a disabled panel is included
                // here: it is invisible to the operator, so counting it would report a problem on every
                // save that nobody can act on. An *active* field that cannot save still reports, from
                // Save(). (The example this was written for was the four Phidgets rows on an inactive
                // PhidgetsPanel; those are deleted, but the skip is what a derived project's own
                // disabled panels rely on, so it stays.)
                if (!_fields[i].gameObject.activeInHierarchy)
                    continue;

                _fields[i].Save();
            }

            // A rejected value is kept out of the model but was left sitting in the input box, so the
            // screen showed what the operator wanted rather than what is in force. Re-reading makes the
            // rejection visible where they are actually looking.
            int rejected = SettingsParse.ErrorCount;

            for (int i = 0; i < _fields.Length; i++)
            {
                if (_fields[i].gameObject.activeInHierarchy)
                    _fields[i].GetData();
            }

            // Neither target selected. Update()'s interactable gate only runs while isVisible, so the
            // button is not reliably disabled during the show animation - and "SETTINGS UPDATED!" for a
            // save that wrote nothing anywhere is the same lie this method used to tell.
            if (!overwriteJsonToggle.isOn && !overwritePrefsToggle.isOn)
            {
                // "Nothing to save" is about *persistence* - the fields above were still applied to the
                // live Core.settings, and any rejection among them still happened. Denying both would be
                // a smaller version of the same lie this method used to tell.
                if (Core.notificationManager != null)
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Info,
                        "NOTHING SAVED TO DISK",
                        rejected > 0
                            ? "Values applied for this session only, and " + rejected + " were rejected - "
                              + "see the log. Tick a target to persist them."
                            : "Values applied for this session only. Tick a target to persist them.");
                }

                return;
            }

            // fileWritten, not "saved": it is true when the operator never asked for a file write, and
            // the PlayerPrefs branch below contributes nothing to it. Naming it "saved" invited exactly
            // the confusion this method already had.
            bool fileWritten = !overwriteJsonToggle.isOn || Core.file.SaveCoreSettings();

            // A file save that the next boot will throw away. Core.Awake prefers PlayerPrefs["Settings"]
            // and gates the file read out entirely, so on a machine carrying that key the write succeeds,
            // the toast said Success, and the edit is discarded permanently - the only trace being the
            // boot error from the *previous* run. Say it here, where the operator is looking.
            bool shadowedByPrefs = overwriteJsonToggle.isOn && PlayerPrefs.HasKey("Settings");

            bool prefsWritten = !overwritePrefsToggle.isOn;

            if (overwritePrefsToggle.isOn)
            {
                // FileManager.SettingsAsJson, not JsonUtility.ToJson: the OneCore command payloads
                // are `object` and JsonUtility cannot emit them, so a plain snapshot silently drops
                // every command_data. That matters more here than in the file writer, because
                // Core.Awake PREFERS this snapshot over settings.json - and on Android and iOS this is
                // the panel's only writer, so one Save would shadow a good file with a lossy snapshot
                // for good.
                string json = FileManager.SettingsAsJson();

                PlayerPrefs.SetString("Settings", json);

                // Save(), which was missing. SetString only queues; Unity flushes at OnApplicationQuit or
                // on pause, so an OS kill or a power cut on a kiosk lost the edit. On Android and iOS this
                // is the *only* writer the panel uses, because the json toggle is hidden there.
                PlayerPrefs.Save();

                // Read back, because both SetString and Save return void. An earlier comment here called
                // that "undetectable by design" - it is not, it just needed checking. Without this,
                // mobile - where the file toggle is hidden, so fileWritten is trivially true - reported
                // Success unconditionally for the only writer the platform has.
                prefsWritten = PlayerPrefs.GetString("Settings") == json;

                if (!prefsWritten)
                {
                    Development.LogError("the settings were not stored: PlayerPrefs did not hold back what "
                        + "was written to it. On this platform that is the panel's only save target, so "
                        + "nothing was persisted.");
                }
            }

            // Reports what happened. This said "SETTINGS UPDATED!" unconditionally - and the unguarded
            // write it used to do could throw out of Save() mid-loop, so the fields after it were never
            // applied either - the exception escapes OnSaveButtonClicked entirely. The
            // unconditional-success defect is real for the silent wrong-path write, which is the
            // case that actually happened on site.
            //
            // `!= null` rather than `?.`: Unity overloads == on Object so a destroyed manager reports as
            // null, and `?.` bypasses that overload - the pattern CLAUDE.md calls out, and the two sibling
            // call sites above already use this form.
            if (Core.notificationManager != null)
            {
                // Both halves reported, and named separately. A write that succeeded while three values
                // were rejected is not a success, and "SETTINGS UPDATED!" for it is a lie.
                if (!fileWritten || !prefsWritten)
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error,
                        "SETTINGS NOT SAVED",
                        !fileWritten
                            ? "The settings file could not be written - check the log."
                            : "The stored settings could not be written - check the log.");
                }
                else if (shadowedByPrefs)
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Warning,
                        "SAVED, BUT WILL BE IGNORED",
                        "This machine has stored settings that override the file. Use Reset Player Prefs, "
                        + "then restart. (M40)");
                }
                else if (rejected > 0)
                {
                    // Not "snapped back": a value rejected for being unparseable does revert visibly, but
                    // an unknown Key has no read-back case either, so that text stays put. The log is the
                    // reliable half.
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Warning,
                        "SAVED, BUT " + rejected + " FIELD(S) DID NOT",
                        "The log names each one.");
                }
                else
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success,
                        "SETTINGS UPDATED!");
                }
            }

            //Hide();
        }
    }
}
