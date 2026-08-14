namespace Tyhp.TyhpLang.Attributes
{
    /// <summary>
    /// Initializes the AST node type registry
    /// </summary>
    public static class AstNodeTypeRegistryInitializer
    {
        private static bool _isInitialized = false;
        
        /// <summary>
        /// Initialize the registry - call this at application startup
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            
            // Initialize the registry
            AstNodeTypeRegistry.Initialize();
            
            _isInitialized = true;
        }
    }
} 