using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using System;

namespace EnigmaVirtual
{
    public enum ValidationType
    {
        None,
        Empty,
        Email,
        Toggle,
        Date,
        ConfirmPassword,
        Length
    }

    public class RegisterField : MonoBehaviour
    {

        public GameObject Warning;
        public InputField Field;
        public InputField ConfirmPasswordField;
        public ToggleGroup Group;
        public int Length = 0;
        public ValidationType Validation;

        public string Format;




        // Use this for initialization
        void Start()
        {
            // Awake used to be `if (!Core.IsSet) return;` and nothing else - a guard that guarded no
            // statement, while THIS line dereferenced Warning ungated regardless. Deleted rather
            // than filled in: an empty guard reads as protection and is the reason nobody noticed the
            // real dereference sitting one method below it.
            //
            // Warning is a [SerializeField], so a null here is an authoring mistake on the prefab, not
            // a runtime condition. Reported rather than allowed to NRE, because "the form silently has
            // no warning label" and "the form threw during Start" look identical from the outside.
            if (!HasWarning())
                return;

            Warning.SetActive(false);
        }

        public void SetData(string value)
        {
            Field.text = value;
        }

        [ContextMenu("GetValue")]
        public string GetValue()
        {
            string value = "";
            switch (Validation)
            {
                case ValidationType.Toggle:
                    value = Group.GetActive().gameObject.name;
                    break;
                case ValidationType.Date:
                    string[] birthdata = Field.text.Split('/');
                    value = birthdata[2] + "-" + birthdata[1] + "-" + birthdata[0];

                    break;
                case ValidationType.None:
                case ValidationType.Empty:
                case ValidationType.Email:
                default:
                    value = Field.text;
                    break;
            }
            print("value: " + value);
            return value;
        }

        /// <summary>
        /// Whether this field can show validation state at all, reported once if it cannot.
        /// </summary>
        /// <remarks>
        /// <c>Warning</c> is a <c>[SerializeField]</c>, so a null is an authoring mistake on the prefab
        /// rather than a runtime condition. Guarded at all three entry points rather than only in
        /// <c>Start</c>: the first version of this fix guarded <c>Start</c> alone and said validation
        /// "cannot be shown or hidden", which was a message describing an outcome that did not happen -
        /// <c>Validate</c> and <c>ShowError</c> still dereferenced it and threw. A log that misdescribes
        /// the failure is worse than no log at all.
        /// </remarks>
        private bool HasWarning()
        {
            if (Warning != null)
                return true;

            if (!warningReported)
            {
                warningReported = true;

                Development.LogError($"{nameof(RegisterField)} on '{gameObject.name}' has no Warning "
                    + "object assigned. This field cannot show or clear its validation message, and "
                    + "Validate() will report it as valid regardless of what was typed.");
            }

            return false;
        }

        private bool warningReported;

        public void ShowError()
        {
            if (!HasWarning())
                return;

            Warning.SetActive(true);
        }

        [ContextMenu("DoValidate")]
        public bool Validate()
        {
            // Returns true - "valid" - when there is no Warning object. That is deliberate and is the
            // lesser evil: the alternative is failing every field on a prefab whose only fault is a
            // missing label, which blocks the form outright. The LogError above says so explicitly.
            if (!HasWarning())
                return true;

            switch (Validation)
            {
                case ValidationType.Empty:
                    Warning.SetActive(Field.text == "");
                    break;
                case ValidationType.Email:
                    Warning.SetActive(!Field.text.IsValidEmail());
                    break;
                case ValidationType.Date:
                    //Warning.SetActive(Field.text.Length != Format.Length);
                    //if (!Warning.activeSelf)
                    //{

                    // KEPT: the only record of what ValidationType.Date was meant to do. That case is still
                    // KEPT: the only record of what ValidationType.Date was meant to do. That case is still
                    // live in the switch above and never touches Warning at all, so Validate() returns
                    // whatever state Warning was left in - a prefab shipping it inactive accepts every date,
                    // one shipping it active rejects every date. Delete this only together with that.
                    //    {
                    //        Development.Log("Valide");
                    //        Warning.SetActive(false);
                    //        string[] birthdata = Field.text.Split('/');
                    //        int year = int.Parse(birthdata[2]);
                    //        if (year>=DateTime.Now.Year || year < 1900)
                    //        {
                    //            Warning.SetActive(true);
                    //        }
                    //    }
                    //    else
                    //    {
                    //        Development.Log("Invalide!");
                    //        Warning.SetActive(true);
                    //    }
                    //}
                    break;

                case ValidationType.ConfirmPassword:
                    Warning.SetActive(Field.text != ConfirmPasswordField.text);
                    break;
                case ValidationType.Length:
                    Warning.SetActive(!(Field.text.Length >= Length));
                    break;
                case ValidationType.None:
                default:
                    Warning.SetActive(false);
                    break;
            }
            return !Warning.activeSelf;
        }
    }
}
