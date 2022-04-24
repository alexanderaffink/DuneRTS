using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterTurnHandler : MonoBehaviour
{

    public static CharacterTurnHandler instance;

    public static bool charSelected { get { return instance.selectedCharacter != null; } }

    private Character selectedCharacter;
    private Character secondCharacter;
    private Actions charState;
    public Actions CharState { get { return charState; } }

    public enum Actions
    {
        ATTACK, MOVE, COLLECT, TRANSFER, KANLY, FAMILY_ATOMICS, SPICE_HOARDING, VOICE, SWORD_SPIN 
    }

    private void Start()
    {
        instance = this;
    }

    public void SelectCharacter(Character character)
    {
        selectedCharacter = character;
    }

    public void SelectSecondCharacter(Character character)
    {
        secondCharacter = character;
    }

    public Character GetSelectedCharacter()
    {
        return selectedCharacter;
    }

    public void ResetSelection()
    {
        selectedCharacter = null;
    }

}
