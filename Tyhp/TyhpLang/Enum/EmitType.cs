namespace Tyhp.TyhpLang.Enum {
    public enum EmitType
    {
        /// <summary>
        /// Things like php blocks and inlinehtml
        /// </summary>
        OutsideItems = 0,

        TyhpBlock = 1,

        /// <summary>
        /// This is for header text at the beginning of a file, usually a comment block
        /// </summary>
        FileHeader = 2,

        /// <summary>
        /// This is for declare() statements at the beginning of the file, not declare block statements.
        /// </summary>
        FileDeclare = 3,

        /// <summary>
        /// This is for the namespace declaration at the top of the file
        /// </summary>
        FileNamespaceDeclaration = 4,

        /// <summary>
        /// This is for the namespace block declaration.  All importUse and rotoStatements happen inside of this block
        /// </summary>
        BlockNamespaceDeclaration = 5,

        /// <summary>
        /// This is the use statements at the top of the file or beginning of the namespace block
        /// </summary>
        ImportUse = 6,

        /// <summary>
        /// This is a root level statement outside of any class or function. This includes class/interface/enum/trait declarations, function declarations, and root level code.
        /// </summary>
        RootStatement = 7,

        ObjectDeclaration = 8,

        /// <summary>
        /// Use statements on a class to include traits to it.
        /// </summary>
        ObjectTraitUse = 9,

        /// <summary>
        /// Declarations od constants on a class
        /// </summary>
        ObjectConstantDeclaration = 10,

        /// <summary>
        /// Declarations of static properties on a class
        /// </summary>
        ObjectStaticPropertyDeclaration = 11,

        /// <summary>
        /// Declarations of instance properties on a class.
        /// </summary>
        ObjectInstancePropertyDeclaration = 12,

        /// <summary>
        /// The constructor method
        /// </summary>
        ObjectConstructor = 13,

        /// <summary>
        /// Teh destructor method
        /// </summary>
        ObjectDestructor = 14,

        /// <summary>
        /// Static methods for the class
        /// </summary>
        ObjectStaticMethods = 15,

        /// <summary>
        /// Instance methods for the class
        /// </summary>
        ObjectInstanceMethods = 16,

        /// <summary>
        /// Global import of variables for the function/method
        /// </summary>
        FunctionGlobalReference = 17,

        /// <summary>
        /// These are statements inside of a function or method
        /// </summary>
        FunctionStatement = 18,

        BlockDeclare = 19,

        /// <summary>
        /// These are statements inside of a sub block
        /// </summary>
        SubBlockStatement = 20,

        OutputFileBlock = 21,
        OutputFileStatement = 21,

        Empty = Int32.MaxValue - 1,
        Group = Int32.MaxValue,
    }
}