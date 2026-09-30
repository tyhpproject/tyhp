namespace Tyhp.Domain.Services
{
    /// <summary>
    /// PDO drivers register <c>MYSQL_*</c> / <c>SQLITE_*</c> / … constants on class
    /// <c>PDO</c> at load time. PHP's <c>ReflectionExtension</c> for <c>pdo</c> then
    /// reports those constants as if they belonged to the core extension, so Layer 1
    /// for <c>tyhpdef/php-ext-pdo</c> collides with driver packages that redeclare them
    /// (TYHP4303). Prefix ownership is the PHP convention (and php.net grouping);
    /// Reflection does not record which extension declared a class constant.
    /// </summary>
    internal static class PdoDriverClassConstants
    {
        /// <summary>
        /// Prefixes as actually registered by php-src / PECL drivers via
        /// <c>REGISTER_PDO_CLASS_CONST_LONG</c> (verified against each driver's C source /
        /// php.net manual page, not guessed from the driver name). Notably there is no
        /// <c>FIREBIRD_</c>, <c>MSSQL_</c>, or <c>INFORMIX_</c> prefix in any shipped driver —
        /// <c>pdo_firebird</c> only registers <c>FB_*</c>, <c>pdo_dblib</c> only registers
        /// <c>DBLIB_*</c>, and <c>pdo_informix</c> registers no driver-specific class constants
        /// at all. <c>pdo_ibm</c> registers legacy names under <c>SQL_*</c> (aliased to
        /// <c>Pdo\Ibm::*</c> as of PHP 8.5), not <c>IBM_*</c>.
        /// </summary>
        private static readonly (string Prefix, string Extension)[] DriverPrefixes =
        [
            ("SQLSRV_", "pdo_sqlsrv"),
            ("CUBRID_", "pdo_cubrid"),
            ("SQLITE_", "pdo_sqlite"),
            ("MYSQL_", "pdo_mysql"),
            ("PGSQL_", "pdo_pgsql"),
            ("DBLIB_", "pdo_dblib"),
            ("ODBC_", "pdo_odbc"),
            ("OCI_", "pdo_oci"),
            ("SQL_", "pdo_ibm"),
            ("FB_", "pdo_firebird"),
        ];

        public static bool IsPdoClass(string? fqn, string? shortName)
        {
            var raw = string.IsNullOrWhiteSpace(fqn) ? shortName : fqn;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw.Trim().TrimStart('\\').Equals("PDO", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPdoExtension(string? extensionName)
            => string.Equals(NormalizeExtension(extensionName), "pdo", StringComparison.Ordinal);

        public static bool IsPdoDriverExtension(string? extensionName)
            => NormalizeExtension(extensionName).StartsWith("pdo_", StringComparison.Ordinal);

        /// <summary>
        /// Whether a constant on class <c>PDO</c> belongs in this extension's Layer 1.
        /// Core constants (<c>ATTR_*</c>, <c>FETCH_*</c>, …) stay on <c>pdo</c>;
        /// driver-prefixed names go only to the matching <c>pdo_*</c> package.
        /// </summary>
        public static bool ShouldIncludePdoClassConstant(string? constantName, string? extensionName)
        {
            if (string.IsNullOrWhiteSpace(constantName))
            {
                return false;
            }

            var owner = TryGetOwnerExtension(constantName);
            if (owner is null)
            {
                return IsPdoExtension(extensionName);
            }

            return string.Equals(owner, NormalizeExtension(extensionName), StringComparison.Ordinal);
        }

        public static string? TryGetOwnerExtension(string constantName)
        {
            foreach (var (prefix, extension) in DriverPrefixes)
            {
                if (constantName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return extension;
                }
            }

            return null;
        }

        private static string NormalizeExtension(string? extensionName)
            => (extensionName ?? "").Trim().ToLowerInvariant();
    }
}
