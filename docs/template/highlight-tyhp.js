/*
Language: Tyhp
Author: Anthony Rainer <anthony@ddress.email>
Description: Tyhp is a strongly typed extension to PHP.
Website: https://www.tyhplang.com
Category: common
*/


hljs.registerLanguage('Tyhp', (hljs) => {
    const tyhpLanguage = hljs.getLanguage('php');

    const TYHP_SPECIFIC_KEYWORDS = [
        "async",
        "await",
        "get",
        "set",
        "lazy",
        "guard",
        "operator",
        "with",
        "extension",
        "type",
        "struct",
        "import",
    ];

    const PREPROCESSOR = {
        scope: 'meta',
        variants: [
            { begin: /<\?tyhpdef/, relevance: 10 },
            { begin: /<\?tyhp/, relevance: 10 },
          { begin: /\?>/ } // end php tag
        ]
      };

    const KEYWORDS = {
        keyword: tyhpLanguage.keywords.keyword.concat(TYHP_SPECIFIC_KEYWORDS),
        literal: tyhpLanguage.keywords.literal,
        built_in: tyhpLanguage.keywords.built_in,
    };

    
    let tyhpContains = tyhpLanguage.contains;
    tyhpContains[5] = PREPROCESSOR;

    Object.assign(tyhpLanguage, {
        name: 'Tyhp',
        aliases: [
            'tyhp',
        ],
        keywords: KEYWORDS,
        contains: tyhpContains,
        });

    return tyhpLanguage;
});