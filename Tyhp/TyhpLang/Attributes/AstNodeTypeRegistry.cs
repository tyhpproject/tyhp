using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tyhp.TyhpLang.Attributes
{
    // Static registry for AST node types.
    // All backing collections are concurrent and Initialize is guarded by a lock so the
    // registry can be safely populated and queried from multiple threads at once (e.g. the
    // parallel parse loop deserializing cached ASTs).
    public static class AstNodeTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, byte> _typeToNodeId = new();
        private static readonly ConcurrentDictionary<byte, Type> _nodeIdToType = new();
        private static readonly ConcurrentDictionary<long, Type> _customHashToType = new();
        private static readonly ConcurrentDictionary<Type, long> _typeToCustomHash = new();

        private static readonly object _initLock = new();
        private static volatile bool _isInitialized = false;
        
        // Custom node type marker
        public const byte CustomNodeTypeByte = 0xFF;
        
        // Initialize the registry by scanning the assembly
        public static void Initialize()
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                // Get all types that extend Base2Ast from the current assembly
                var assembly = typeof(Ast.Base2Ast).Assembly;
                var nodeTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Ast.Base2Ast)))
                    .OrderBy(t => t.FullName) // Ensure consistent ordering
                    .ToList();

                // Assign IDs starting from 0
                byte nextId = 0;
                foreach (var type in nodeTypes)
                {
                    // Skip if we've already hit the max
                    if (nextId == CustomNodeTypeByte)
                        throw new Exception($"Too many AST node types. Maximum is 0x{CustomNodeTypeByte - 1:X2}");

                    _typeToNodeId[type] = nextId;
                    _nodeIdToType[nextId] = type;
                    nextId++;
                }

                _isInitialized = true;
            }
        }
        
        // Get node type ID for a specific type
        public static byte GetNodeTypeId(Type type)
        {
            if (!_isInitialized) Initialize();
            
            if (_typeToNodeId.TryGetValue(type, out byte id))
                return id;
                
            // If type is not in our registry but extends Base2Ast, it's a custom type
            if (type.IsSubclassOf(typeof(Ast.Base2Ast)))
                return CustomNodeTypeByte;
                
            throw new ArgumentException($"Type {type.FullName} is not an AST node type");
        }
        
        // Get type for a specific node type ID
        public static Type GetTypeForNodeId(byte id)
        {
            if (!_isInitialized) Initialize();
            
            if (_nodeIdToType.TryGetValue(id, out Type? type))
                return type;
                
            // Custom types handled separately
            if (id == CustomNodeTypeByte)
                throw new ArgumentException("Cannot get type for custom node ID without custom type info");
                
            throw new ArgumentException($"No AST node type registered for ID 0x{id:X2}");
        }
        
        // Generate a consistent hash for custom types
        public static long GetCustomTypeHash(Type type)
        {
            if (_typeToCustomHash.TryGetValue(type, out long hash))
                return hash;
                
            // Using type's full name for hash calculation
            string fullName = type.FullName ?? type.Name;
            hash = GetInt64Hash(fullName);
            
            // Register the hash for this type
            RegisterCustomType(type, hash);
            
            return hash;
        }

        private static long GetInt64Hash(string strText)
        {
            if (!String.IsNullOrEmpty(strText)) {
                byte[] byteContents = System.Text.Encoding.Unicode.GetBytes(strText);
                using var hash = System.Security.Cryptography.SHA256.Create();
                byte[] hashText = hash.ComputeHash(byteContents);
                return BitConverter.ToInt64(hashText, 0) ^
                    BitConverter.ToInt64(hashText, 8) ^
                    BitConverter.ToInt64(hashText, 16) ^
                    BitConverter.ToInt64(hashText, 24);
            }
            return 0L;
        }
        
        // Register a custom type with its hash for deserialization
        public static void RegisterCustomType(Type type, long customHash)
        {
            _customHashToType[customHash] = type;
            _typeToCustomHash[type] = customHash;
        }
        
        // Get custom type from hash
        public static Type GetCustomType(long customHash)
        {
            if (_customHashToType.TryGetValue(customHash, out Type? type))
                return type;
            
            var typesThatHashToCustomHash =
                from assembly in AppDomain.CurrentDomain.GetAssemblies().AsParallel()
                from asmType in assembly.GetTypes()
                where GetCustomTypeHash(asmType) == customHash
                select asmType;

            type = typesThatHashToCustomHash.FirstOrDefault();
            if (type != null) {
                RegisterCustomType(type, customHash);
                return type;
            }
            throw new ArgumentException($"No custom AST node type registered for hash {customHash}");
        }
    }
} 