using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace EscapeFromDuckovCoopMod.Patch
{
    public static class CharacterItemAccessor
    {
        private static System.Reflection.FieldInfo _characterItemField;
        
        static CharacterItemAccessor()
        {
            _characterItemField = typeof(CharacterMainControl).GetField("characterItem", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
        }
        
        public static void SetCharacterItem(CharacterMainControl character, object item)
        {
            if (character == null) return;
            
            try
            {
                _characterItemField?.SetValue(character, item);
            }
            catch
            {
            }
        }
        
        public static object GetCharacterItem(CharacterMainControl character)
        {
            if (character == null) return null;
            
            try
            {
                return _characterItemField?.GetValue(character);
            }
            catch
            {
                return null;
            }
        }
    }
    
    [HarmonyPatch(typeof(CharacterSpawnerRoot))]
    public static class CharacterSpawnerAccessor
    {
        public static bool GetInited(CharacterSpawnerRoot root)
        {
            if (root == null) return false;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("inited", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                return field != null && (bool)field.GetValue(root);
            }
            catch
            {
                return false;
            }
        }
        
        public static void SetInited(CharacterSpawnerRoot root, bool value)
        {
            if (root == null) return;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("inited", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                field?.SetValue(root, value);
            }
            catch
            {
            }
        }
        
        public static bool GetCreated(CharacterSpawnerRoot root)
        {
            if (root == null) return false;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("created", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                return field != null && (bool)field.GetValue(root);
            }
            catch
            {
                return false;
            }
        }
        
        public static CharacterSpawnerComponentBase GetSpawnerComponent(CharacterSpawnerRoot root)
        {
            if (root == null) return null;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("spawnerComponent", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                return field?.GetValue(root) as CharacterSpawnerComponentBase;
            }
            catch
            {
                return null;
            }
        }
        
        public static void SetRelatedScene(CharacterSpawnerRoot root, int sceneIndex)
        {
            if (root == null) return;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("relatedScene", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                field?.SetValue(root, sceneIndex);
            }
            catch
            {
            }
        }
        
        public static System.Collections.Generic.List<CharacterMainControl> GetCreatedCharacters(CharacterSpawnerRoot root)
        {
            if (root == null) return null;
            
            try
            {
                var field = typeof(CharacterSpawnerRoot).GetField("createdCharacters", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                return field?.GetValue(root) as System.Collections.Generic.List<CharacterMainControl>;
            }
            catch
            {
                return null;
            }
        }
    }
}

