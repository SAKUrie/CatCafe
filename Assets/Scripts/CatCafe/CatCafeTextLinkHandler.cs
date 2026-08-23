using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ManyFace.CatCafe
{
    /// <summary>
    /// Builds real Unity Buttons over TextMeshPro link glyphs. Only linked words receive
    /// raycasts, so ordinary rule copy naturally falls through to a parent card Button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CatCafeTextLinkHandler : MonoBehaviour
    {
        private readonly List<GameObject> linkButtons = new List<GameObject>();
        private TMP_Text targetText;
        private Action<string, PointerEventData> linkClicked;
        private string builtText;
        private Vector2 builtRectSize = new Vector2(float.NaN, float.NaN);
        private float builtFontSize = float.NaN;

        public void Initialize(TMP_Text text, Action<string, PointerEventData> onLinkClicked)
        {
            targetText = text;
            linkClicked = onLinkClicked;
            builtText = null;
            if (targetText != null) targetText.raycastTarget = false;
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases += RefreshLinkButtons;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= RefreshLinkButtons;
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= RefreshLinkButtons;
        }

        private void RefreshLinkButtons()
        {
            if (targetText == null || linkClicked == null) return;

            RectTransform textRect = targetText.rectTransform;
            Vector2 rectSize = textRect.rect.size;
            if (string.Equals(builtText, targetText.text, StringComparison.Ordinal) &&
                Approximately(builtRectSize, rectSize) &&
                Mathf.Approximately(builtFontSize, targetText.fontSize))
            {
                return;
            }

            targetText.ForceMeshUpdate();
            ClearLinkButtons();
            targetText.raycastTarget = false;

            TMP_TextInfo textInfo = targetText.textInfo;
            for (int linkIndex = 0; linkIndex < textInfo.linkCount; linkIndex++)
            {
                TMP_LinkInfo link = textInfo.linkInfo[linkIndex];
                string linkId = link.GetLinkID();
                if (string.IsNullOrEmpty(linkId)) continue;

                int first = link.linkTextfirstCharacterIndex;
                int last = Mathf.Min(first + link.linkTextLength, textInfo.characterCount);
                int currentLine = -1;
                bool hasBounds = false;
                float minX = 0f;
                float maxX = 0f;
                float minY = 0f;
                float maxY = 0f;

                for (int characterIndex = first; characterIndex < last; characterIndex++)
                {
                    if (characterIndex < 0 || characterIndex >= textInfo.characterInfo.Length) continue;
                    TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];
                    if (hasBounds && character.lineNumber != currentLine)
                    {
                        CreateLinkButton(linkId, linkIndex, currentLine, minX, maxX, minY, maxY);
                        hasBounds = false;
                    }

                    float left = Mathf.Min(character.origin, character.bottomLeft.x);
                    float right = Mathf.Max(character.xAdvance, character.topRight.x);
                    float bottom = Mathf.Min(character.descender, character.bottomLeft.y);
                    float top = Mathf.Max(character.ascender, character.topRight.y);
                    if (!hasBounds)
                    {
                        currentLine = character.lineNumber;
                        minX = left;
                        maxX = right;
                        minY = bottom;
                        maxY = top;
                        hasBounds = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, left);
                        maxX = Mathf.Max(maxX, right);
                        minY = Mathf.Min(minY, bottom);
                        maxY = Mathf.Max(maxY, top);
                    }
                }

                if (hasBounds)
                {
                    CreateLinkButton(linkId, linkIndex, currentLine, minX, maxX, minY, maxY);
                }
            }

            builtText = targetText.text;
            builtRectSize = rectSize;
            builtFontSize = targetText.fontSize;
        }

        private void CreateLinkButton(string linkId, int linkIndex, int lineIndex,
            float minX, float maxX, float minY, float maxY)
        {
            if (maxX <= minX || maxY <= minY) return;

            GameObject buttonObject = new GameObject(
                "TMP Link Button " + linkIndex + "_" + lineIndex,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(EventTrigger));
            buttonObject.transform.SetParent(targetText.transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            Vector2 parentPivot = targetText.rectTransform.pivot;
            rect.anchorMin = parentPivot;
            rect.anchorMax = parentPivot;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            rect.sizeDelta = new Vector2(maxX - minX, maxY - minY);

            Image image = buttonObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            EventTrigger trigger = buttonObject.GetComponent<EventTrigger>();
            EventTrigger.Entry click = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            click.callback.AddListener(delegate(BaseEventData data)
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer != null && linkClicked != null) linkClicked(linkId, pointer);
            });
            trigger.triggers.Add(click);
            linkButtons.Add(buttonObject);
        }

        private void ClearLinkButtons()
        {
            for (int i = 0; i < linkButtons.Count; i++)
            {
                GameObject button = linkButtons[i];
                if (button == null) continue;
                button.SetActive(false);
                Destroy(button);
            }
            linkButtons.Clear();
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }
    }
}
