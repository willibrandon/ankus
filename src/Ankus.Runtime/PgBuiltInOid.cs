// Regenerate from the pinned pgrx catalogs with eng/Ankus.Oids.cs.
namespace Ankus;

/// <summary>
/// Names numeric constants from pgrx's PostgreSQL 13–19 built-in OID catalogs.
/// </summary>
/// <remarks>
/// Members retain exact unsigned values. Renamed symbols share one member, named after the newest source spelling.
/// Use PgBuiltInOids for version-aware conversion and native names. A member does not imply availability in every
/// server version, catalog existence, or object category. The source naming heuristic also includes non-object constants.
/// Zero is not a defined member. Ordinary PostgreSQL oid datums continue to use uint, independently of this catalog.
/// </remarks>
public enum PgBuiltInOid : uint
{
    /// <summary>
    /// The <c>TemplateDbOid</c> constant (<c>1</c>).
    /// </summary>
    /// <remarks>
    /// <c>TemplateDbOid</c> in PostgreSQL 13, 14.
    /// </remarks>
    TemplateDbOid = 1,

    /// <summary>
    /// The <c>HEAP_TABLE_AM_OID</c> constant (<c>2</c>).
    /// </summary>
    /// <remarks>
    /// <c>HEAP_TABLE_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    HeapTableAmOid = 2,

    /// <summary>
    /// The <c>HEAP_TABLE_AM_HANDLER_OID</c> constant (<c>3</c>).
    /// </summary>
    /// <remarks>
    /// <c>HEAP_TABLE_AM_HANDLER_OID</c> in PostgreSQL 13.
    /// </remarks>
    HeapTableAmHandlerOid = 3,

    /// <summary>
    /// The <c>PROGRESS_CREATEIDX_INDEX_OID</c> constant (<c>6</c>).
    /// </summary>
    /// <remarks>
    /// <c>PROGRESS_CREATEIDX_INDEX_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ProgressCreateidxIndexOid = 6,

    /// <summary>
    /// The <c>PROGRESS_CREATEIDX_ACCESS_METHOD_OID</c> constant (<c>8</c>).
    /// </summary>
    /// <remarks>
    /// <c>PROGRESS_CREATEIDX_ACCESS_METHOD_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ProgressCreateidxAccessMethodOid = 8,

    /// <summary>
    /// The <c>BOOLOID</c> constant (<c>16</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOOLOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoolOid = 16,

    /// <summary>
    /// The <c>BYTEAOID</c> constant (<c>17</c>).
    /// </summary>
    /// <remarks>
    /// <c>BYTEAOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ByteaOid = 17,

    /// <summary>
    /// The <c>CHAROID</c> constant (<c>18</c>).
    /// </summary>
    /// <remarks>
    /// <c>CHAROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CharOid = 18,

    /// <summary>
    /// The <c>NAMEOID</c> constant (<c>19</c>).
    /// </summary>
    /// <remarks>
    /// <c>NAMEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NameOid = 19,

    /// <summary>
    /// The <c>INT8OID</c> constant (<c>20</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8Oid = 20,

    /// <summary>
    /// The <c>INT2OID</c> constant (<c>21</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT2OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int2Oid = 21,

    /// <summary>
    /// The <c>INT2VECTOROID</c> constant (<c>22</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT2VECTOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int2VectorOid = 22,

    /// <summary>
    /// The <c>INT4OID</c> constant (<c>23</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4Oid = 23,

    /// <summary>
    /// The <c>REGPROCOID</c> constant (<c>24</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGPROCOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegProcOid = 24,

    /// <summary>
    /// The <c>TEXTOID</c> constant (<c>25</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextOid = 25,

    /// <summary>
    /// The <c>OIDOID</c> constant (<c>26</c>).
    /// </summary>
    /// <remarks>
    /// <c>OIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidOid = 26,

    /// <summary>
    /// The <c>TIDOID</c> constant (<c>27</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TidOid = 27,

    /// <summary>
    /// The <c>XIDOID</c> constant (<c>28</c>).
    /// </summary>
    /// <remarks>
    /// <c>XIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    XidOid = 28,

    /// <summary>
    /// The <c>CIDOID</c> constant (<c>29</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CidOid = 29,

    /// <summary>
    /// The <c>OIDVECTOROID</c> constant (<c>30</c>).
    /// </summary>
    /// <remarks>
    /// <c>OIDVECTOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidVectorOid = 30,

    /// <summary>
    /// The <c>PG_DDL_COMMANDOID</c> constant (<c>32</c>).
    /// </summary>
    /// <remarks>
    /// <c>PGDDLCOMMANDOID</c> in PostgreSQL 13.
    /// <c>PG_DDL_COMMANDOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgDdlCommandOid = 32,

    /// <summary>
    /// The <c>XLOG_NEXTOID</c> constant (<c>48</c>).
    /// </summary>
    /// <remarks>
    /// <c>XLOG_NEXTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    XlogNextOid = 48,

    /// <summary>
    /// The <c>DEFAULT_COLLATION_OID</c> constant (<c>100</c>).
    /// </summary>
    /// <remarks>
    /// <c>DEFAULT_COLLATION_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DefaultCollationOid = 100,

    /// <summary>
    /// The <c>JSONOID</c> constant (<c>114</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonOid = 114,

    /// <summary>
    /// The <c>XMLOID</c> constant (<c>142</c>).
    /// </summary>
    /// <remarks>
    /// <c>XMLOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    XmlOid = 142,

    /// <summary>
    /// The <c>XMLARRAYOID</c> constant (<c>143</c>).
    /// </summary>
    /// <remarks>
    /// <c>XMLARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    XmlArrayOid = 143,

    /// <summary>
    /// The <c>PG_NODE_TREEOID</c> constant (<c>194</c>).
    /// </summary>
    /// <remarks>
    /// <c>PGNODETREEOID</c> in PostgreSQL 13.
    /// <c>PG_NODE_TREEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgNodeTreeOid = 194,

    /// <summary>
    /// The <c>JSONARRAYOID</c> constant (<c>199</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonArrayOid = 199,

    /// <summary>
    /// The <c>PG_TYPEARRAYOID</c> constant (<c>210</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_TYPEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgTypeArrayOid = 210,

    /// <summary>
    /// The <c>F_NAMECONCATOID</c> constant (<c>266</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_NAMECONCATOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionNameconcatOid = 266,

    /// <summary>
    /// The <c>TABLE_AM_HANDLEROID</c> constant (<c>269</c>).
    /// </summary>
    /// <remarks>
    /// <c>TABLE_AM_HANDLEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TableAmHandlerOid = 269,

    /// <summary>
    /// The <c>PG_ATTRIBUTEARRAYOID</c> constant (<c>270</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_ATTRIBUTEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgAttributeArrayOid = 270,

    /// <summary>
    /// The <c>XID8ARRAYOID</c> constant (<c>271</c>).
    /// </summary>
    /// <remarks>
    /// <c>XID8ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Xid8ArrayOid = 271,

    /// <summary>
    /// The <c>PG_PROCARRAYOID</c> constant (<c>272</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_PROCARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgProcArrayOid = 272,

    /// <summary>
    /// The <c>PG_CLASSARRAYOID</c> constant (<c>273</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_CLASSARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgClassArrayOid = 273,

    /// <summary>
    /// The <c>F_PG_NEXTOID</c> constant (<c>275</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_NEXTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgNextOid = 275,

    /// <summary>
    /// The <c>INDEX_AM_HANDLEROID</c> constant (<c>325</c>).
    /// </summary>
    /// <remarks>
    /// <c>INDEX_AM_HANDLEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IndexAmHandlerOid = 325,

    /// <summary>
    /// The <c>BTREE_AM_OID</c> constant (<c>403</c>).
    /// </summary>
    /// <remarks>
    /// <c>BTREE_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BTreeAmOid = 403,

    /// <summary>
    /// The <c>HASH_AM_OID</c> constant (<c>405</c>).
    /// </summary>
    /// <remarks>
    /// <c>HASH_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    HashAmOid = 405,

    /// <summary>
    /// The <c>BOOL_BTREE_FAM_OID</c> constant (<c>424</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOOL_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoolBTreeFamOid = 424,

    /// <summary>
    /// The <c>BPCHAR_BTREE_FAM_OID</c> constant (<c>426</c>).
    /// </summary>
    /// <remarks>
    /// <c>BPCHAR_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BpCharBTreeFamOid = 426,

    /// <summary>
    /// The <c>BYTEA_BTREE_FAM_OID</c> constant (<c>428</c>).
    /// </summary>
    /// <remarks>
    /// <c>BYTEA_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ByteaBTreeFamOid = 428,

    /// <summary>
    /// The <c>F_HASHOID</c> constant (<c>453</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_HASHOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionHashOid = 453,

    /// <summary>
    /// The <c>POINTOID</c> constant (<c>600</c>).
    /// </summary>
    /// <remarks>
    /// <c>POINTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PointOid = 600,

    /// <summary>
    /// The <c>LSEGOID</c> constant (<c>601</c>).
    /// </summary>
    /// <remarks>
    /// <c>LSEGOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    LsegOid = 601,

    /// <summary>
    /// The <c>PATHOID</c> constant (<c>602</c>).
    /// </summary>
    /// <remarks>
    /// <c>PATHOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PathOid = 602,

    /// <summary>
    /// The <c>BOXOID</c> constant (<c>603</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOXOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoxOid = 603,

    /// <summary>
    /// The <c>POLYGONOID</c> constant (<c>604</c>).
    /// </summary>
    /// <remarks>
    /// <c>POLYGONOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PolygonOid = 604,

    /// <summary>
    /// The <c>LINEOID</c> constant (<c>628</c>).
    /// </summary>
    /// <remarks>
    /// <c>LINEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    LineOid = 628,

    /// <summary>
    /// The <c>LINEARRAYOID</c> constant (<c>629</c>).
    /// </summary>
    /// <remarks>
    /// <c>LINEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    LineArrayOid = 629,

    /// <summary>
    /// The <c>CIDROID</c> constant (<c>650</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIDROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CidrOid = 650,

    /// <summary>
    /// The <c>CIDRARRAYOID</c> constant (<c>651</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIDRARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CidrArrayOid = 651,

    /// <summary>
    /// The <c>FLOAT4OID</c> constant (<c>700</c>).
    /// </summary>
    /// <remarks>
    /// <c>FLOAT4OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Float4Oid = 700,

    /// <summary>
    /// The <c>FLOAT8OID</c> constant (<c>701</c>).
    /// </summary>
    /// <remarks>
    /// <c>FLOAT8OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Float8Oid = 701,

    /// <summary>
    /// The <c>UNKNOWNOID</c> constant (<c>705</c>).
    /// </summary>
    /// <remarks>
    /// <c>UNKNOWNOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    UnknownOid = 705,

    /// <summary>
    /// The <c>CIRCLEOID</c> constant (<c>718</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIRCLEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CircleOid = 718,

    /// <summary>
    /// The <c>CIRCLEARRAYOID</c> constant (<c>719</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIRCLEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CircleArrayOid = 719,

    /// <summary>
    /// The <c>F_LO_IMPORT_TEXT_OID</c> constant (<c>767</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BE_LO_IMPORT_WITH_OID</c> in PostgreSQL 13.
    /// <c>F_LO_IMPORT_TEXT_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionLoImportTextOid = 767,

    /// <summary>
    /// The <c>MACADDR8OID</c> constant (<c>774</c>).
    /// </summary>
    /// <remarks>
    /// <c>MACADDR8OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Macaddr8Oid = 774,

    /// <summary>
    /// The <c>MACADDR8ARRAYOID</c> constant (<c>775</c>).
    /// </summary>
    /// <remarks>
    /// <c>MACADDR8ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Macaddr8ArrayOid = 775,

    /// <summary>
    /// The <c>GIST_AM_OID</c> constant (<c>783</c>).
    /// </summary>
    /// <remarks>
    /// <c>GIST_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    GistAmOid = 783,

    /// <summary>
    /// The <c>MONEYOID</c> constant (<c>790</c>).
    /// </summary>
    /// <remarks>
    /// <c>CASHOID</c> in PostgreSQL 13.
    /// <c>MONEYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    MoneyOid = 790,

    /// <summary>
    /// The <c>MONEYARRAYOID</c> constant (<c>791</c>).
    /// </summary>
    /// <remarks>
    /// <c>MONEYARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    MoneyArrayOid = 791,

    /// <summary>
    /// The <c>MACADDROID</c> constant (<c>829</c>).
    /// </summary>
    /// <remarks>
    /// <c>MACADDROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    MacaddrOid = 829,

    /// <summary>
    /// The <c>INETOID</c> constant (<c>869</c>).
    /// </summary>
    /// <remarks>
    /// <c>INETOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    InetOid = 869,

    /// <summary>
    /// The <c>C_COLLATION_OID</c> constant (<c>950</c>).
    /// </summary>
    /// <remarks>
    /// <c>C_COLLATION_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CCollationOid = 950,

    /// <summary>
    /// The <c>POSIX_COLLATION_OID</c> constant (<c>951</c>).
    /// </summary>
    /// <remarks>
    /// <c>POSIX_COLLATION_OID</c> in PostgreSQL 13, 14, 15, 16, 17.
    /// </remarks>
    PosixCollationOid = 951,

    /// <summary>
    /// The <c>BOOLARRAYOID</c> constant (<c>1000</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOOLARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoolArrayOid = 1000,

    /// <summary>
    /// The <c>BYTEAARRAYOID</c> constant (<c>1001</c>).
    /// </summary>
    /// <remarks>
    /// <c>BYTEAARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ByteaArrayOid = 1001,

    /// <summary>
    /// The <c>CHARARRAYOID</c> constant (<c>1002</c>).
    /// </summary>
    /// <remarks>
    /// <c>CHARARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CharArrayOid = 1002,

    /// <summary>
    /// The <c>NAMEARRAYOID</c> constant (<c>1003</c>).
    /// </summary>
    /// <remarks>
    /// <c>NAMEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NameArrayOid = 1003,

    /// <summary>
    /// The <c>INT2ARRAYOID</c> constant (<c>1005</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT2ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int2ArrayOid = 1005,

    /// <summary>
    /// The <c>INT2VECTORARRAYOID</c> constant (<c>1006</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT2VECTORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int2VectorArrayOid = 1006,

    /// <summary>
    /// The <c>INT4ARRAYOID</c> constant (<c>1007</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4ArrayOid = 1007,

    /// <summary>
    /// The <c>REGPROCARRAYOID</c> constant (<c>1008</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGPROCARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegProcArrayOid = 1008,

    /// <summary>
    /// The <c>TEXTARRAYOID</c> constant (<c>1009</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXTARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextArrayOid = 1009,

    /// <summary>
    /// The <c>TIDARRAYOID</c> constant (<c>1010</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TidArrayOid = 1010,

    /// <summary>
    /// The <c>XIDARRAYOID</c> constant (<c>1011</c>).
    /// </summary>
    /// <remarks>
    /// <c>XIDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    XidArrayOid = 1011,

    /// <summary>
    /// The <c>CIDARRAYOID</c> constant (<c>1012</c>).
    /// </summary>
    /// <remarks>
    /// <c>CIDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CidArrayOid = 1012,

    /// <summary>
    /// The <c>OIDVECTORARRAYOID</c> constant (<c>1013</c>).
    /// </summary>
    /// <remarks>
    /// <c>OIDVECTORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidVectorArrayOid = 1013,

    /// <summary>
    /// The <c>BPCHARARRAYOID</c> constant (<c>1014</c>).
    /// </summary>
    /// <remarks>
    /// <c>BPCHARARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BpCharArrayOid = 1014,

    /// <summary>
    /// The <c>VARCHARARRAYOID</c> constant (<c>1015</c>).
    /// </summary>
    /// <remarks>
    /// <c>VARCHARARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VarcharArrayOid = 1015,

    /// <summary>
    /// The <c>INT8ARRAYOID</c> constant (<c>1016</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8ArrayOid = 1016,

    /// <summary>
    /// The <c>POINTARRAYOID</c> constant (<c>1017</c>).
    /// </summary>
    /// <remarks>
    /// <c>POINTARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PointArrayOid = 1017,

    /// <summary>
    /// The <c>LSEGARRAYOID</c> constant (<c>1018</c>).
    /// </summary>
    /// <remarks>
    /// <c>LSEGARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    LsegArrayOid = 1018,

    /// <summary>
    /// The <c>PATHARRAYOID</c> constant (<c>1019</c>).
    /// </summary>
    /// <remarks>
    /// <c>PATHARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PathArrayOid = 1019,

    /// <summary>
    /// The <c>BOXARRAYOID</c> constant (<c>1020</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOXARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoxArrayOid = 1020,

    /// <summary>
    /// The <c>FLOAT4ARRAYOID</c> constant (<c>1021</c>).
    /// </summary>
    /// <remarks>
    /// <c>FLOAT4ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Float4ArrayOid = 1021,

    /// <summary>
    /// The <c>FLOAT8ARRAYOID</c> constant (<c>1022</c>).
    /// </summary>
    /// <remarks>
    /// <c>FLOAT8ARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Float8ArrayOid = 1022,

    /// <summary>
    /// The <c>POLYGONARRAYOID</c> constant (<c>1027</c>).
    /// </summary>
    /// <remarks>
    /// <c>POLYGONARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PolygonArrayOid = 1027,

    /// <summary>
    /// The <c>OIDARRAYOID</c> constant (<c>1028</c>).
    /// </summary>
    /// <remarks>
    /// <c>OIDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidArrayOid = 1028,

    /// <summary>
    /// The <c>ACLITEMOID</c> constant (<c>1033</c>).
    /// </summary>
    /// <remarks>
    /// <c>ACLITEMOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AclItemOid = 1033,

    /// <summary>
    /// The <c>ACLITEMARRAYOID</c> constant (<c>1034</c>).
    /// </summary>
    /// <remarks>
    /// <c>ACLITEMARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AclItemArrayOid = 1034,

    /// <summary>
    /// The <c>MACADDRARRAYOID</c> constant (<c>1040</c>).
    /// </summary>
    /// <remarks>
    /// <c>MACADDRARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    MacaddrArrayOid = 1040,

    /// <summary>
    /// The <c>INETARRAYOID</c> constant (<c>1041</c>).
    /// </summary>
    /// <remarks>
    /// <c>INETARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    InetArrayOid = 1041,

    /// <summary>
    /// The <c>BPCHAROID</c> constant (<c>1042</c>).
    /// </summary>
    /// <remarks>
    /// <c>BPCHAROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BpCharOid = 1042,

    /// <summary>
    /// The <c>VARCHAROID</c> constant (<c>1043</c>).
    /// </summary>
    /// <remarks>
    /// <c>VARCHAROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VarcharOid = 1043,

    /// <summary>
    /// The <c>DATEOID</c> constant (<c>1082</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateOid = 1082,

    /// <summary>
    /// The <c>TIMEOID</c> constant (<c>1083</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimeOid = 1083,

    /// <summary>
    /// The <c>TIMESTAMPOID</c> constant (<c>1114</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestampOid = 1114,

    /// <summary>
    /// The <c>TIMESTAMPARRAYOID</c> constant (<c>1115</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestampArrayOid = 1115,

    /// <summary>
    /// The <c>DATEARRAYOID</c> constant (<c>1182</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateArrayOid = 1182,

    /// <summary>
    /// The <c>TIMEARRAYOID</c> constant (<c>1183</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimeArrayOid = 1183,

    /// <summary>
    /// The <c>TIMESTAMPTZOID</c> constant (<c>1184</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPTZOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestamptzOid = 1184,

    /// <summary>
    /// The <c>TIMESTAMPTZARRAYOID</c> constant (<c>1185</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPTZARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestamptzArrayOid = 1185,

    /// <summary>
    /// The <c>INTERVALOID</c> constant (<c>1186</c>).
    /// </summary>
    /// <remarks>
    /// <c>INTERVALOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IntervalOid = 1186,

    /// <summary>
    /// The <c>INTERVALARRAYOID</c> constant (<c>1187</c>).
    /// </summary>
    /// <remarks>
    /// <c>INTERVALARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IntervalArrayOid = 1187,

    /// <summary>
    /// The <c>TableSpaceRelationId</c> constant (<c>1213</c>).
    /// </summary>
    /// <remarks>
    /// <c>TableSpaceRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TableSpaceRelationId = 1213,

    /// <summary>
    /// The <c>NUMERICARRAYOID</c> constant (<c>1231</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMERICARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumericArrayOid = 1231,

    /// <summary>
    /// The <c>TypeRelationId</c> constant (<c>1247</c>).
    /// </summary>
    /// <remarks>
    /// <c>TypeRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TypeRelationId = 1247,

    /// <summary>
    /// The <c>AttributeRelationId</c> constant (<c>1249</c>).
    /// </summary>
    /// <remarks>
    /// <c>AttributeRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AttributeRelationId = 1249,

    /// <summary>
    /// The <c>ProcedureRelationId</c> constant (<c>1255</c>).
    /// </summary>
    /// <remarks>
    /// <c>ProcedureRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ProcedureRelationId = 1255,

    /// <summary>
    /// The <c>RelationRelationId</c> constant (<c>1259</c>).
    /// </summary>
    /// <remarks>
    /// <c>RelationRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RelationRelationId = 1259,

    /// <summary>
    /// The <c>AuthIdRelationId</c> constant (<c>1260</c>).
    /// </summary>
    /// <remarks>
    /// <c>AuthIdRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AuthIdRelationId = 1260,

    /// <summary>
    /// The <c>AuthMemRelationId</c> constant (<c>1261</c>).
    /// </summary>
    /// <remarks>
    /// <c>AuthMemRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AuthMemRelationId = 1261,

    /// <summary>
    /// The <c>DatabaseRelationId</c> constant (<c>1262</c>).
    /// </summary>
    /// <remarks>
    /// <c>DatabaseRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DatabaseRelationId = 1262,

    /// <summary>
    /// The <c>CSTRINGARRAYOID</c> constant (<c>1263</c>).
    /// </summary>
    /// <remarks>
    /// <c>CSTRINGARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CStringArrayOid = 1263,

    /// <summary>
    /// The <c>TIMETZOID</c> constant (<c>1266</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMETZOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimetzOid = 1266,

    /// <summary>
    /// The <c>TIMETZARRAYOID</c> constant (<c>1270</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMETZARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimetzArrayOid = 1270,

    /// <summary>
    /// The <c>F_OID</c> constant (<c>1287</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_I8TOOID</c> in PostgreSQL 13.
    /// <c>F_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionOid = 1287,

    /// <summary>
    /// The <c>F_INT8_OID</c> constant (<c>1288</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_INT8_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionInt8Oid = 1288,

    /// <summary>
    /// The <c>F_CURRTID_BYRELOID</c> constant (<c>1293</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_CURRTID_BYRELOID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionCurrtidByrelOid = 1293,

    /// <summary>
    /// The <c>F_OBJ_DESCRIPTION_OID</c> constant (<c>1348</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_OBJ_DESCRIPTION_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionObjDescriptionOid = 1348,

    /// <summary>
    /// The <c>F_PG_GET_CONSTRAINTDEF_OID</c> constant (<c>1387</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_CONSTRAINTDEF_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetConstraintdefOid = 1387,

    /// <summary>
    /// The <c>ForeignServerRelationId</c> constant (<c>1417</c>).
    /// </summary>
    /// <remarks>
    /// <c>ForeignServerRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ForeignServerRelationId = 1417,

    /// <summary>
    /// The <c>UserMappingRelationId</c> constant (<c>1418</c>).
    /// </summary>
    /// <remarks>
    /// <c>UserMappingRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    UserMappingRelationId = 1418,

    /// <summary>
    /// The <c>BITOID</c> constant (<c>1560</c>).
    /// </summary>
    /// <remarks>
    /// <c>BITOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BitOid = 1560,

    /// <summary>
    /// The <c>BITARRAYOID</c> constant (<c>1561</c>).
    /// </summary>
    /// <remarks>
    /// <c>BITARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BitArrayOid = 1561,

    /// <summary>
    /// The <c>VARBITOID</c> constant (<c>1562</c>).
    /// </summary>
    /// <remarks>
    /// <c>VARBITOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VarbitOid = 1562,

    /// <summary>
    /// The <c>VARBITARRAYOID</c> constant (<c>1563</c>).
    /// </summary>
    /// <remarks>
    /// <c>VARBITARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VarbitArrayOid = 1563,

    /// <summary>
    /// The <c>F_PG_GET_RULEDEF_OID</c> constant (<c>1573</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_RULEDEF_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetRuledefOid = 1573,

    /// <summary>
    /// The <c>F_NEXTVAL_OID</c> constant (<c>1574</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_NEXTVAL_OID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionNextvalOid = 1574,

    /// <summary>
    /// The <c>F_CURRVAL_OID</c> constant (<c>1575</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_CURRVAL_OID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionCurrvalOid = 1575,

    /// <summary>
    /// The <c>F_SETVAL_OID</c> constant (<c>1576</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_SETVAL_OID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionSetvalOid = 1576,

    /// <summary>
    /// The <c>F_PG_GET_VIEWDEF_OID</c> constant (<c>1641</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_VIEWDEF_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetViewdefOid = 1641,

    /// <summary>
    /// The <c>F_PG_GET_INDEXDEF_OID</c> constant (<c>1643</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_INDEXDEF_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetIndexdefOid = 1643,

    /// <summary>
    /// The <c>F_PG_GET_TRIGGERDEF_OID</c> constant (<c>1662</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_TRIGGERDEF_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetTriggerdefOid = 1662,

    /// <summary>
    /// The <c>DEFAULTTABLESPACE_OID</c> constant (<c>1663</c>).
    /// </summary>
    /// <remarks>
    /// <c>DEFAULTTABLESPACE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DefaultTablespaceOid = 1663,

    /// <summary>
    /// The <c>GLOBALTABLESPACE_OID</c> constant (<c>1664</c>).
    /// </summary>
    /// <remarks>
    /// <c>GLOBALTABLESPACE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    GlobalTablespaceOid = 1664,

    /// <summary>
    /// The <c>NUMERICOID</c> constant (<c>1700</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMERICOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumericOid = 1700,

    /// <summary>
    /// The <c>F_PG_GET_EXPR_PG_NODE_TREE_OID</c> constant (<c>1716</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_EXPR_PG_NODE_TREE_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgGetExprPgNodeTreeOid = 1716,

    /// <summary>
    /// The <c>F_SETVAL3_OID</c> constant (<c>1765</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_SETVAL3_OID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionSetval3Oid = 1765,

    /// <summary>
    /// The <c>REFCURSOROID</c> constant (<c>1790</c>).
    /// </summary>
    /// <remarks>
    /// <c>REFCURSOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RefCursorOid = 1790,

    /// <summary>
    /// The <c>NETWORK_BTREE_FAM_OID</c> constant (<c>1974</c>).
    /// </summary>
    /// <remarks>
    /// <c>NETWORK_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NetworkBTreeFamOid = 1974,

    /// <summary>
    /// The <c>INTEGER_BTREE_FAM_OID</c> constant (<c>1976</c>).
    /// </summary>
    /// <remarks>
    /// <c>INTEGER_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IntegerBTreeFamOid = 1976,

    /// <summary>
    /// The <c>INT4_BTREE_OPS_OID</c> constant (<c>1978</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4BTreeOpsOid = 1978,

    /// <summary>
    /// The <c>INT2_BTREE_OPS_OID</c> constant (<c>1979</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT2_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int2BTreeOpsOid = 1979,

    /// <summary>
    /// The <c>OID_BTREE_OPS_OID</c> constant (<c>1981</c>).
    /// </summary>
    /// <remarks>
    /// <c>OID_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidBTreeOpsOid = 1981,

    /// <summary>
    /// The <c>INTERVAL_BTREE_FAM_OID</c> constant (<c>1982</c>).
    /// </summary>
    /// <remarks>
    /// <c>INTERVAL_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IntervalBTreeFamOid = 1982,

    /// <summary>
    /// The <c>OID_BTREE_FAM_OID</c> constant (<c>1989</c>).
    /// </summary>
    /// <remarks>
    /// <c>OID_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OidBTreeFamOid = 1989,

    /// <summary>
    /// The <c>TEXT_BTREE_FAM_OID</c> constant (<c>1994</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXT_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextBTreeFamOid = 1994,

    /// <summary>
    /// The <c>TEXT_PATTERN_BTREE_FAM_OID</c> constant (<c>2095</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXT_PATTERN_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextPatternBTreeFamOid = 2095,

    /// <summary>
    /// The <c>BPCHAR_PATTERN_BTREE_FAM_OID</c> constant (<c>2097</c>).
    /// </summary>
    /// <remarks>
    /// <c>BPCHAR_PATTERN_BTREE_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BpCharPatternBTreeFamOid = 2097,

    /// <summary>
    /// The <c>F_MAX_OID</c> constant (<c>2118</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_MAX_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionMaxOid = 2118,

    /// <summary>
    /// The <c>F_MIN_OID</c> constant (<c>2134</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_MIN_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionMinOid = 2134,

    /// <summary>
    /// The <c>REFCURSORARRAYOID</c> constant (<c>2201</c>).
    /// </summary>
    /// <remarks>
    /// <c>REFCURSORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RefCursorArrayOid = 2201,

    /// <summary>
    /// The <c>REGPROCEDUREOID</c> constant (<c>2202</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGPROCEDUREOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegProcedureOid = 2202,

    /// <summary>
    /// The <c>REGOPEROID</c> constant (<c>2203</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGOPEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegOperOid = 2203,

    /// <summary>
    /// The <c>REGOPERATOROID</c> constant (<c>2204</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGOPERATOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegOperatorOid = 2204,

    /// <summary>
    /// The <c>REGCLASSOID</c> constant (<c>2205</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCLASSOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegClassOid = 2205,

    /// <summary>
    /// The <c>REGTYPEOID</c> constant (<c>2206</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGTYPEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegTypeOid = 2206,

    /// <summary>
    /// The <c>REGPROCEDUREARRAYOID</c> constant (<c>2207</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGPROCEDUREARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegProcedureArrayOid = 2207,

    /// <summary>
    /// The <c>REGOPERARRAYOID</c> constant (<c>2208</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGOPERARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegOperArrayOid = 2208,

    /// <summary>
    /// The <c>REGOPERATORARRAYOID</c> constant (<c>2209</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGOPERATORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegOperatorArrayOid = 2209,

    /// <summary>
    /// The <c>REGCLASSARRAYOID</c> constant (<c>2210</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCLASSARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegClassArrayOid = 2210,

    /// <summary>
    /// The <c>REGTYPEARRAYOID</c> constant (<c>2211</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGTYPEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegTypeArrayOid = 2211,

    /// <summary>
    /// The <c>BOOL_HASH_FAM_OID</c> constant (<c>2222</c>).
    /// </summary>
    /// <remarks>
    /// <c>BOOL_HASH_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BoolHashFamOid = 2222,

    /// <summary>
    /// The <c>RECORDOID</c> constant (<c>2249</c>).
    /// </summary>
    /// <remarks>
    /// <c>RECORDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RecordOid = 2249,

    /// <summary>
    /// The <c>CSTRINGOID</c> constant (<c>2275</c>).
    /// </summary>
    /// <remarks>
    /// <c>CSTRINGOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CStringOid = 2275,

    /// <summary>
    /// The <c>ANYOID</c> constant (<c>2276</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyOid = 2276,

    /// <summary>
    /// The <c>ANYARRAYOID</c> constant (<c>2277</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyArrayOid = 2277,

    /// <summary>
    /// The <c>VOIDOID</c> constant (<c>2278</c>).
    /// </summary>
    /// <remarks>
    /// <c>VOIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VoidOid = 2278,

    /// <summary>
    /// The <c>TRIGGEROID</c> constant (<c>2279</c>).
    /// </summary>
    /// <remarks>
    /// <c>TRIGGEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TriggerOid = 2279,

    /// <summary>
    /// The <c>LANGUAGE_HANDLEROID</c> constant (<c>2280</c>).
    /// </summary>
    /// <remarks>
    /// <c>LANGUAGE_HANDLEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    LanguageHandlerOid = 2280,

    /// <summary>
    /// The <c>INTERNALOID</c> constant (<c>2281</c>).
    /// </summary>
    /// <remarks>
    /// <c>INTERNALOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    InternalOid = 2281,

    /// <summary>
    /// The <c>ANYELEMENTOID</c> constant (<c>2283</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYELEMENTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyElementOid = 2283,

    /// <summary>
    /// The <c>RECORDARRAYOID</c> constant (<c>2287</c>).
    /// </summary>
    /// <remarks>
    /// <c>RECORDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RecordArrayOid = 2287,

    /// <summary>
    /// The <c>F_PG_TABLESPACE_SIZE_OID</c> constant (<c>2322</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_TABLESPACE_SIZE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgTablespaceSizeOid = 2322,

    /// <summary>
    /// The <c>F_PG_DATABASE_SIZE_OID</c> constant (<c>2324</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_DATABASE_SIZE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgDatabaseSizeOid = 2324,

    /// <summary>
    /// The <c>ForeignDataWrapperRelationId</c> constant (<c>2328</c>).
    /// </summary>
    /// <remarks>
    /// <c>ForeignDataWrapperRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ForeignDataWrapperRelationId = 2328,

    /// <summary>
    /// The <c>AccessMethodRelationId</c> constant (<c>2601</c>).
    /// </summary>
    /// <remarks>
    /// <c>AccessMethodRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AccessMethodRelationId = 2601,

    /// <summary>
    /// The <c>AccessMethodOperatorRelationId</c> constant (<c>2602</c>).
    /// </summary>
    /// <remarks>
    /// <c>AccessMethodOperatorRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AccessMethodOperatorRelationId = 2602,

    /// <summary>
    /// The <c>AccessMethodProcedureRelationId</c> constant (<c>2603</c>).
    /// </summary>
    /// <remarks>
    /// <c>AccessMethodProcedureRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AccessMethodProcedureRelationId = 2603,

    /// <summary>
    /// The <c>IndexRelationId</c> constant (<c>2610</c>).
    /// </summary>
    /// <remarks>
    /// <c>IndexRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    IndexRelationId = 2610,

    /// <summary>
    /// The <c>NamespaceRelationId</c> constant (<c>2615</c>).
    /// </summary>
    /// <remarks>
    /// <c>NamespaceRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NamespaceRelationId = 2615,

    /// <summary>
    /// The <c>OperatorClassRelationId</c> constant (<c>2616</c>).
    /// </summary>
    /// <remarks>
    /// <c>OperatorClassRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OperatorClassRelationId = 2616,

    /// <summary>
    /// The <c>OperatorRelationId</c> constant (<c>2617</c>).
    /// </summary>
    /// <remarks>
    /// <c>OperatorRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OperatorRelationId = 2617,

    /// <summary>
    /// The <c>StatisticRelationId</c> constant (<c>2619</c>).
    /// </summary>
    /// <remarks>
    /// <c>StatisticRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    StatisticRelationId = 2619,

    /// <summary>
    /// The <c>TriggerRelationId</c> constant (<c>2620</c>).
    /// </summary>
    /// <remarks>
    /// <c>TriggerRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TriggerRelationId = 2620,

    /// <summary>
    /// The <c>GIN_AM_OID</c> constant (<c>2742</c>).
    /// </summary>
    /// <remarks>
    /// <c>GIN_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    GinAmOid = 2742,

    /// <summary>
    /// The <c>OperatorFamilyRelationId</c> constant (<c>2753</c>).
    /// </summary>
    /// <remarks>
    /// <c>OperatorFamilyRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    OperatorFamilyRelationId = 2753,

    /// <summary>
    /// The <c>ANYNONARRAYOID</c> constant (<c>2776</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYNONARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyNonArrayOid = 2776,

    /// <summary>
    /// The <c>TXID_SNAPSHOTARRAYOID</c> constant (<c>2949</c>).
    /// </summary>
    /// <remarks>
    /// <c>TXID_SNAPSHOTARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TxidSnapshotArrayOid = 2949,

    /// <summary>
    /// The <c>UUIDOID</c> constant (<c>2950</c>).
    /// </summary>
    /// <remarks>
    /// <c>UUIDOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    UuidOid = 2950,

    /// <summary>
    /// The <c>UUIDARRAYOID</c> constant (<c>2951</c>).
    /// </summary>
    /// <remarks>
    /// <c>UUIDARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    UuidArrayOid = 2951,

    /// <summary>
    /// The <c>TXID_SNAPSHOTOID</c> constant (<c>2970</c>).
    /// </summary>
    /// <remarks>
    /// <c>TXID_SNAPSHOTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TxidSnapshotOid = 2970,

    /// <summary>
    /// The <c>ExtensionRelationId</c> constant (<c>3079</c>).
    /// </summary>
    /// <remarks>
    /// <c>ExtensionRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ExtensionRelationId = 3079,

    /// <summary>
    /// The <c>FDW_HANDLEROID</c> constant (<c>3115</c>).
    /// </summary>
    /// <remarks>
    /// <c>FDW_HANDLEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FdwHandlerOid = 3115,

    /// <summary>
    /// The <c>ForeignTableRelationId</c> constant (<c>3118</c>).
    /// </summary>
    /// <remarks>
    /// <c>ForeignTableRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ForeignTableRelationId = 3118,

    /// <summary>
    /// The <c>DATE_BTREE_OPS_OID</c> constant (<c>3122</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATE_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateBTreeOpsOid = 3122,

    /// <summary>
    /// The <c>FLOAT8_BTREE_OPS_OID</c> constant (<c>3123</c>).
    /// </summary>
    /// <remarks>
    /// <c>FLOAT8_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Float8BTreeOpsOid = 3123,

    /// <summary>
    /// The <c>INT8_BTREE_OPS_OID</c> constant (<c>3124</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8BTreeOpsOid = 3124,

    /// <summary>
    /// The <c>NUMERIC_BTREE_OPS_OID</c> constant (<c>3125</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMERIC_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumericBTreeOpsOid = 3125,

    /// <summary>
    /// The <c>TEXT_BTREE_OPS_OID</c> constant (<c>3126</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXT_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextBTreeOpsOid = 3126,

    /// <summary>
    /// The <c>TIMESTAMPTZ_BTREE_OPS_OID</c> constant (<c>3127</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMPTZ_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestamptzBTreeOpsOid = 3127,

    /// <summary>
    /// The <c>TIMESTAMP_BTREE_OPS_OID</c> constant (<c>3128</c>).
    /// </summary>
    /// <remarks>
    /// <c>TIMESTAMP_BTREE_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TimestampBTreeOpsOid = 3128,

    /// <summary>
    /// The <c>PG_LSNOID</c> constant (<c>3220</c>).
    /// </summary>
    /// <remarks>
    /// <c>LSNOID</c> in PostgreSQL 13.
    /// <c>PG_LSNOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgLsnOid = 3220,

    /// <summary>
    /// The <c>PG_LSNARRAYOID</c> constant (<c>3221</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_LSNARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgLsnArrayOid = 3221,

    /// <summary>
    /// The <c>F_ROW_SECURITY_ACTIVE_OID</c> constant (<c>3298</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_ROW_SECURITY_ACTIVE_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionRowSecurityActiveOid = 3298,

    /// <summary>
    /// The <c>TSM_HANDLEROID</c> constant (<c>3310</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSM_HANDLEROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsmHandlerOid = 3310,

    /// <summary>
    /// The <c>PG_NDISTINCTOID</c> constant (<c>3361</c>).
    /// </summary>
    /// <remarks>
    /// <c>PGNDISTINCTOID</c> in PostgreSQL 13.
    /// <c>PG_NDISTINCTOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgNDistinctOid = 3361,

    /// <summary>
    /// The <c>StatisticExtRelationId</c> constant (<c>3381</c>).
    /// </summary>
    /// <remarks>
    /// <c>StatisticExtRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    StatisticExtRelationId = 3381,

    /// <summary>
    /// The <c>PG_DEPENDENCIESOID</c> constant (<c>3402</c>).
    /// </summary>
    /// <remarks>
    /// <c>PGDEPENDENCIESOID</c> in PostgreSQL 13.
    /// <c>PG_DEPENDENCIESOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgDependenciesOid = 3402,

    /// <summary>
    /// The <c>CollationRelationId</c> constant (<c>3456</c>).
    /// </summary>
    /// <remarks>
    /// <c>CollationRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    CollationRelationId = 3456,

    /// <summary>
    /// The <c>F_LO_GET_OID</c> constant (<c>3458</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_LO_GET_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionLoGetOid = 3458,

    /// <summary>
    /// The <c>EventTriggerRelationId</c> constant (<c>3466</c>).
    /// </summary>
    /// <remarks>
    /// <c>EventTriggerRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    EventTriggerRelationId = 3466,

    /// <summary>
    /// The <c>ANYENUMOID</c> constant (<c>3500</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYENUMOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyEnumOid = 3500,

    /// <summary>
    /// The <c>EnumRelationId</c> constant (<c>3501</c>).
    /// </summary>
    /// <remarks>
    /// <c>EnumRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    EnumRelationId = 3501,

    /// <summary>
    /// The <c>BRIN_AM_OID</c> constant (<c>3580</c>).
    /// </summary>
    /// <remarks>
    /// <c>BRIN_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BrinAmOid = 3580,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_PG_TYPE_OID</c> constant (<c>3582</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_PG_TYPE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextPgTypeOid = 3582,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_ARRAY_PG_TYPE_OID</c> constant (<c>3584</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_ARRAY_PG_TYPE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextArrayPgTypeOid = 3584,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_TOAST_PG_TYPE_OID</c> constant (<c>3585</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_TOAST_PG_TYPE_OID</c> in PostgreSQL 13.
    /// </remarks>
    FunctionBinaryUpgradeSetNextToastPgTypeOid = 3585,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_HEAP_PG_CLASS_OID</c> constant (<c>3586</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_HEAP_PG_CLASS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextHeapPgClassOid = 3586,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_INDEX_PG_CLASS_OID</c> constant (<c>3587</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_INDEX_PG_CLASS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextIndexPgClassOid = 3587,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_TOAST_PG_CLASS_OID</c> constant (<c>3588</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_TOAST_PG_CLASS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextToastPgClassOid = 3588,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_PG_ENUM_OID</c> constant (<c>3589</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_PG_ENUM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextPgEnumOid = 3589,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_PG_AUTHID_OID</c> constant (<c>3590</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_PG_AUTHID_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextPgAuthIdOid = 3590,

    /// <summary>
    /// The <c>SecLabelRelationId</c> constant (<c>3596</c>).
    /// </summary>
    /// <remarks>
    /// <c>SecLabelRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    SecLabelRelationId = 3596,

    /// <summary>
    /// The <c>TSVECTOROID</c> constant (<c>3614</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSVECTOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsVectorOid = 3614,

    /// <summary>
    /// The <c>TSQUERYOID</c> constant (<c>3615</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSQUERYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsQueryOid = 3615,

    /// <summary>
    /// The <c>GTSVECTOROID</c> constant (<c>3642</c>).
    /// </summary>
    /// <remarks>
    /// <c>GTSVECTOROID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    GtsVectorOid = 3642,

    /// <summary>
    /// The <c>TSVECTORARRAYOID</c> constant (<c>3643</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSVECTORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsVectorArrayOid = 3643,

    /// <summary>
    /// The <c>GTSVECTORARRAYOID</c> constant (<c>3644</c>).
    /// </summary>
    /// <remarks>
    /// <c>GTSVECTORARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    GtsVectorArrayOid = 3644,

    /// <summary>
    /// The <c>TSQUERYARRAYOID</c> constant (<c>3645</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSQUERYARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsQueryArrayOid = 3645,

    /// <summary>
    /// The <c>F_TS_TOKEN_TYPE_OID</c> constant (<c>3713</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_TS_TOKEN_TYPE_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionTsTokenTypeOid = 3713,

    /// <summary>
    /// The <c>REGCONFIGOID</c> constant (<c>3734</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCONFIGOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegConfigOid = 3734,

    /// <summary>
    /// The <c>REGCONFIGARRAYOID</c> constant (<c>3735</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCONFIGARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegConfigArrayOid = 3735,

    /// <summary>
    /// The <c>REGDICTIONARYOID</c> constant (<c>3769</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGDICTIONARYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegDictionaryOid = 3769,

    /// <summary>
    /// The <c>REGDICTIONARYARRAYOID</c> constant (<c>3770</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGDICTIONARYARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegDictionaryArrayOid = 3770,

    /// <summary>
    /// The <c>JSONBOID</c> constant (<c>3802</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONBOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonbOid = 3802,

    /// <summary>
    /// The <c>JSONBARRAYOID</c> constant (<c>3807</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONBARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonbArrayOid = 3807,

    /// <summary>
    /// The <c>ANYRANGEOID</c> constant (<c>3831</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYRANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyRangeOid = 3831,

    /// <summary>
    /// The <c>EVENT_TRIGGEROID</c> constant (<c>3838</c>).
    /// </summary>
    /// <remarks>
    /// <c>EVTTRIGGEROID</c> in PostgreSQL 13.
    /// <c>EVENT_TRIGGEROID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    EventTriggerOid = 3838,

    /// <summary>
    /// The <c>INT4RANGEOID</c> constant (<c>3904</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4RANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4RangeOid = 3904,

    /// <summary>
    /// The <c>INT4RANGEARRAYOID</c> constant (<c>3905</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4RANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4RangeArrayOid = 3905,

    /// <summary>
    /// The <c>NUMRANGEOID</c> constant (<c>3906</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMRANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumRangeOid = 3906,

    /// <summary>
    /// The <c>NUMRANGEARRAYOID</c> constant (<c>3907</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMRANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumRangeArrayOid = 3907,

    /// <summary>
    /// The <c>TSRANGEOID</c> constant (<c>3908</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSRANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsRangeOid = 3908,

    /// <summary>
    /// The <c>TSRANGEARRAYOID</c> constant (<c>3909</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSRANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsRangeArrayOid = 3909,

    /// <summary>
    /// The <c>TSTZRANGEOID</c> constant (<c>3910</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSTZRANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TstzRangeOid = 3910,

    /// <summary>
    /// The <c>TSTZRANGEARRAYOID</c> constant (<c>3911</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSTZRANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TstzRangeArrayOid = 3911,

    /// <summary>
    /// The <c>DATERANGEOID</c> constant (<c>3912</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATERANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateRangeOid = 3912,

    /// <summary>
    /// The <c>DATERANGEARRAYOID</c> constant (<c>3913</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATERANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateRangeArrayOid = 3913,

    /// <summary>
    /// The <c>INT8RANGEOID</c> constant (<c>3926</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8RANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8RangeOid = 3926,

    /// <summary>
    /// The <c>INT8RANGEARRAYOID</c> constant (<c>3927</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8RANGEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8RangeArrayOid = 3927,

    /// <summary>
    /// The <c>SPGIST_AM_OID</c> constant (<c>4000</c>).
    /// </summary>
    /// <remarks>
    /// <c>SPGIST_AM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    SpGistAmOid = 4000,

    /// <summary>
    /// The <c>TEXT_SPGIST_FAM_OID</c> constant (<c>4017</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXT_SPGIST_FAM_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextSpGistFamOid = 4017,

    /// <summary>
    /// The <c>JSONPATHOID</c> constant (<c>4072</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONPATHOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonPathOid = 4072,

    /// <summary>
    /// The <c>JSONPATHARRAYOID</c> constant (<c>4073</c>).
    /// </summary>
    /// <remarks>
    /// <c>JSONPATHARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    JsonPathArrayOid = 4073,

    /// <summary>
    /// The <c>REGNAMESPACEOID</c> constant (<c>4089</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGNAMESPACEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegNamespaceOid = 4089,

    /// <summary>
    /// The <c>REGNAMESPACEARRAYOID</c> constant (<c>4090</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGNAMESPACEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegNamespaceArrayOid = 4090,

    /// <summary>
    /// The <c>REGROLEOID</c> constant (<c>4096</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGROLEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegRoleOid = 4096,

    /// <summary>
    /// The <c>REGROLEARRAYOID</c> constant (<c>4097</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGROLEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegRoleArrayOid = 4097,

    /// <summary>
    /// The <c>REGCOLLATIONOID</c> constant (<c>4191</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCOLLATIONOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegCollationOid = 4191,

    /// <summary>
    /// The <c>REGCOLLATIONARRAYOID</c> constant (<c>4192</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGCOLLATIONARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    RegCollationArrayOid = 4192,

    /// <summary>
    /// The <c>TEXT_BTREE_PATTERN_OPS_OID</c> constant (<c>4217</c>).
    /// </summary>
    /// <remarks>
    /// <c>TEXT_BTREE_PATTERN_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TextBTreePatternOpsOid = 4217,

    /// <summary>
    /// The <c>VARCHAR_BTREE_PATTERN_OPS_OID</c> constant (<c>4218</c>).
    /// </summary>
    /// <remarks>
    /// <c>VARCHAR_BTREE_PATTERN_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    VarcharBTreePatternOpsOid = 4218,

    /// <summary>
    /// The <c>BPCHAR_BTREE_PATTERN_OPS_OID</c> constant (<c>4219</c>).
    /// </summary>
    /// <remarks>
    /// <c>BPCHAR_BTREE_PATTERN_OPS_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    BpCharBTreePatternOpsOid = 4219,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_MULTIRANGE_PG_TYPE_OID</c> constant (<c>4390</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_MULTIRANGE_PG_TYPE_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextMultirangePgTypeOid = 4390,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_MULTIRANGE_ARRAY_PG_TYPE_OID</c> constant (<c>4391</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_MULTIRANGE_ARRAY_PG_TYPE_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextMultirangeArrayPgTypeOid = 4391,

    /// <summary>
    /// The <c>INT4MULTIRANGEOID</c> constant (<c>4451</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4MULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4MultirangeOid = 4451,

    /// <summary>
    /// The <c>NUMMULTIRANGEOID</c> constant (<c>4532</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumMultirangeOid = 4532,

    /// <summary>
    /// The <c>TSMULTIRANGEOID</c> constant (<c>4533</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsMultirangeOid = 4533,

    /// <summary>
    /// The <c>TSTZMULTIRANGEOID</c> constant (<c>4534</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSTZMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TstzMultirangeOid = 4534,

    /// <summary>
    /// The <c>DATEMULTIRANGEOID</c> constant (<c>4535</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATEMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateMultirangeOid = 4535,

    /// <summary>
    /// The <c>INT8MULTIRANGEOID</c> constant (<c>4536</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8MULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8MultirangeOid = 4536,

    /// <summary>
    /// The <c>ANYMULTIRANGEOID</c> constant (<c>4537</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyMultirangeOid = 4537,

    /// <summary>
    /// The <c>ANYCOMPATIBLEMULTIRANGEOID</c> constant (<c>4538</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYCOMPATIBLEMULTIRANGEOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyCompatibleMultirangeOid = 4538,

    /// <summary>
    /// The <c>F_BINARY_UPGRADE_SET_NEXT_PG_TABLESPACE_OID</c> constant (<c>4548</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_BINARY_UPGRADE_SET_NEXT_PG_TABLESPACE_OID</c> in PostgreSQL 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionBinaryUpgradeSetNextPgTablespaceOid = 4548,

    /// <summary>
    /// The <c>F_PG_EVENT_TRIGGER_TABLE_REWRITE_OID</c> constant (<c>4566</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_EVENT_TRIGGER_TABLE_REWRITE_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgEventTriggerTableRewriteOid = 4566,

    /// <summary>
    /// The <c>PG_BRIN_BLOOM_SUMMARYOID</c> constant (<c>4600</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_BRIN_BLOOM_SUMMARYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgBrinBloomSummaryOid = 4600,

    /// <summary>
    /// The <c>PG_BRIN_MINMAX_MULTI_SUMMARYOID</c> constant (<c>4601</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_BRIN_MINMAX_MULTI_SUMMARYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgBrinMinMaxMultiSummaryOid = 4601,

    /// <summary>
    /// The <c>PG_MCV_LISTOID</c> constant (<c>5017</c>).
    /// </summary>
    /// <remarks>
    /// <c>PGMCVLISTOID</c> in PostgreSQL 13.
    /// <c>PG_MCV_LISTOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgMcvListOid = 5017,

    /// <summary>
    /// The <c>F_PG_LS_TMPDIR_OID</c> constant (<c>5030</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_LS_TMPDIR_OID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgLsTmpdirOid = 5030,

    /// <summary>
    /// The <c>PG_SNAPSHOTOID</c> constant (<c>5038</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_SNAPSHOTOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgSnapshotOid = 5038,

    /// <summary>
    /// The <c>PG_SNAPSHOTARRAYOID</c> constant (<c>5039</c>).
    /// </summary>
    /// <remarks>
    /// <c>PG_SNAPSHOTARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PgSnapshotArrayOid = 5039,

    /// <summary>
    /// The <c>XID8OID</c> constant (<c>5069</c>).
    /// </summary>
    /// <remarks>
    /// <c>XID8OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Xid8Oid = 5069,

    /// <summary>
    /// The <c>ANYCOMPATIBLEOID</c> constant (<c>5077</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYCOMPATIBLEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyCompatibleOid = 5077,

    /// <summary>
    /// The <c>ANYCOMPATIBLEARRAYOID</c> constant (<c>5078</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYCOMPATIBLEARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyCompatibleArrayOid = 5078,

    /// <summary>
    /// The <c>ANYCOMPATIBLENONARRAYOID</c> constant (<c>5079</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYCOMPATIBLENONARRAYOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyCompatibleNonArrayOid = 5079,

    /// <summary>
    /// The <c>ANYCOMPATIBLERANGEOID</c> constant (<c>5080</c>).
    /// </summary>
    /// <remarks>
    /// <c>ANYCOMPATIBLERANGEOID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    AnyCompatibleRangeOid = 5080,

    /// <summary>
    /// The <c>ReplicationOriginRelationId</c> constant (<c>6000</c>).
    /// </summary>
    /// <remarks>
    /// <c>ReplicationOriginRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    ReplicationOriginRelationId = 6000,

    /// <summary>
    /// The <c>F_PG_REPLICATION_ORIGIN_OID</c> constant (<c>6005</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_REPLICATION_ORIGIN_OID</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    FunctionPgReplicationOriginOid = 6005,

    /// <summary>
    /// The <c>PublicationRelationId</c> constant (<c>6104</c>).
    /// </summary>
    /// <remarks>
    /// <c>PublicationRelationId</c> in PostgreSQL 13, 14, 15, 16, 17, 18, 19.
    /// </remarks>
    PublicationRelationId = 6104,

    /// <summary>
    /// The <c>INT4MULTIRANGEARRAYOID</c> constant (<c>6150</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT4MULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int4MultirangeArrayOid = 6150,

    /// <summary>
    /// The <c>NUMMULTIRANGEARRAYOID</c> constant (<c>6151</c>).
    /// </summary>
    /// <remarks>
    /// <c>NUMMULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    NumMultirangeArrayOid = 6151,

    /// <summary>
    /// The <c>TSMULTIRANGEARRAYOID</c> constant (<c>6152</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSMULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TsMultirangeArrayOid = 6152,

    /// <summary>
    /// The <c>TSTZMULTIRANGEARRAYOID</c> constant (<c>6153</c>).
    /// </summary>
    /// <remarks>
    /// <c>TSTZMULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    TstzMultirangeArrayOid = 6153,

    /// <summary>
    /// The <c>DATEMULTIRANGEARRAYOID</c> constant (<c>6155</c>).
    /// </summary>
    /// <remarks>
    /// <c>DATEMULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    DateMultirangeArrayOid = 6155,

    /// <summary>
    /// The <c>INT8MULTIRANGEARRAYOID</c> constant (<c>6157</c>).
    /// </summary>
    /// <remarks>
    /// <c>INT8MULTIRANGEARRAYOID</c> in PostgreSQL 14, 15, 16, 17, 18, 19.
    /// </remarks>
    Int8MultirangeArrayOid = 6157,

    /// <summary>
    /// The <c>F_PG_GET_PUBLICATION_TABLES__TEXT_OID</c> constant (<c>6435</c>).
    /// </summary>
    /// <remarks>
    /// <c>F_PG_GET_PUBLICATION_TABLES__TEXT_OID</c> in PostgreSQL 19.
    /// </remarks>
    FunctionPgGetPublicationTablesTextOid = 6435,

    /// <summary>
    /// The <c>OID8OID</c> constant (<c>6437</c>).
    /// </summary>
    /// <remarks>
    /// <c>OID8OID</c> in PostgreSQL 19.
    /// </remarks>
    Oid8Oid = 6437,

    /// <summary>
    /// The <c>OID8ARRAYOID</c> constant (<c>6442</c>).
    /// </summary>
    /// <remarks>
    /// <c>OID8ARRAYOID</c> in PostgreSQL 19.
    /// </remarks>
    Oid8ArrayOid = 6442,

    /// <summary>
    /// The <c>REGDATABASEOID</c> constant (<c>6490</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGDATABASEOID</c> in PostgreSQL 19.
    /// </remarks>
    RegDatabaseOid = 6490,

    /// <summary>
    /// The <c>REGDATABASEARRAYOID</c> constant (<c>6491</c>).
    /// </summary>
    /// <remarks>
    /// <c>REGDATABASEARRAYOID</c> in PostgreSQL 19.
    /// </remarks>
    RegDatabaseArrayOid = 6491,
}
