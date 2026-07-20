<?xml version="1.0"?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:msxsl="urn:schemas-microsoft-com:xslt" version="1.0" xmlns:mssqlcrt="urn:sql-microsoft-com:sqlcrt" xmlns:mssqldbg="urn:sql-microsoft-com:sqldbg">
	<xsl:output method="xml" indent="yes"/>

	<!-- ======================================================================
	     ClickHouse pluggable cartridge for SQL Server Analysis Services
	     (Multidimensional) — DRAFT, pending empirical verification through
	     the manual SSAS smoke validation (docs/ssas-smoke-checklist.md).

	     SSAS MD generates relational SQL by transforming an internal XML
	     query tree with an XSL cartridge picked from the Cartridges folder
	     next to the engine binary (server: [SSAS]\OLAP\bin\Cartridges;
	     design time: the Analysis Services Projects extension folders —
	     see deploy/install-cartridge.ps1 and docs/deploy-windows.md).
	     Selection key: "{DataSourceProductName}.{DataSourceProductVersion}"
	     from the provider's GetSchema("DataSourceInformation"), matched
	     against the mssqlcrt:provider elements below (exact match first,
	     then longest prefix; the empty prefix in sql2000.xsl is the
	     universal fallback).

	     Structure follows the de-facto cartridge format of the files shipped
	     with SSAS (sql2000.xsl as the plumbing baseline; hive.xsl for the
	     LIMIT dialect; the community Npgsql cartridge as the third-party
	     ADO.NET precedent). The format is not documented by Microsoft;
	     sources:
	       - F. Jehl, "Providers, DSV et XSL cartridges" (2013),
	         fjehl.wordpress.com
	       - shipped cartridges of SSAS 2012/2022 (sql2000.xsl, hive.xsl)
	       - decompiled Microsoft.AnalysisServices.BackEnd RDMSCartridge /
	         DataSourceUtilities (cartridge selection + xsl:param harvesting)
	       - naviy/smarttravel Npgsql.xsl (PostgreSQL third-party cartridge)
	       - Vertica forum posts on vertica.xsl (identifier-quoting params,
	         SSAS service restart requirement)

	     ClickHouse dialect decisions:
	       - identifiers quoted with double quotes ("x"); NEVER [brackets]
	         (brackets are the array constructor in ClickHouse)
	       - string literals in single quotes; embedded quotes doubled
	         ('' is valid ClickHouse escaping)
	       - row limiting is LIMIT n placed after ORDER BY (no TOP);
	         supports-top-clause stays declared so SSAS keeps emitting Top
	         nodes — the same pattern Microsoft's own hive.xsl uses
	       - UNION must be spelled UNION DISTINCT (bare UNION requires the
	         union_default_mode server setting)
	       - no writeback/materialized-view/OpenRowset capabilities:
	         the provider is import/processing-only, and multi-source DSVs
	         (OpenRowset) are unavailable to managed providers
	       - parametric queries disabled: the provider does not accept
	         @named/? parameter markers in SQL text (ClickHouse HTTP uses
	         {name:Type} placeholders), so literals are inlined via the
	         non-parametric path below.
	         TODO(ssas-smoke): revisit if @-marker support is added.

	     Dependency: cartridge selection requires the provider to return a
	     DataSourceInformation schema collection whose DataSourceProductName
	     starts with "ClickHouse". GetSchema currently implements only
	     "Columns" — this must land before the cartridge can be selected.

	     TODO(ssas-smoke): the whole file is a draft compiled from the
	     sources above without a live SSAS run; validate every marked spot
	     and record findings in docs/ssas-smoke-checklist.md.
	     ================================================================== -->

	<!-- Area of Custom parametrizations: may be modified for specific query
	     customizations. post-select-query-hint appends a custom string to
	     every generated SELECT — e.g. a ClickHouse SETTINGS clause. -->
	<xsl:param name="post-select-query-hint"></xsl:param>

	<!-- Area of STANDARD parametrizations: these are externally passed -->
	<!-- in_CanUseParams=no: inline literals; see header. -->
	<xsl:param name="in_CanUseParams">no</xsl:param>
	<xsl:param name="in_IdentStartQuotingCharacter">"</xsl:param>
	<xsl:param name="in_IdentEndQuotingCharacter">"</xsl:param>
	<xsl:param name="in_StringStartQuotingCharacter">'</xsl:param>
	<xsl:param name="in_StringEndQuotingCharacter">'</xsl:param>
	<!-- Date literal format for the host ({0} is a .NET format placeholder;
	     pattern follows the Teradata cartridge trdtv2r41.xsl).
	     TODO(ssas-smoke): confirm the host applies this for date
	     restrictions with an ADO.NET provider. -->
	<xsl:param name="in_DateValueFormat">CAST('{0:yyyy-MM-dd HH':'mm':'ss}' AS DateTime)</xsl:param>

	<!-- Area of CORE parametrizations: These are externally checked -->
	<!-- Matches GetSchema("DataSourceInformation").DataSourceProductName
	     (prefix match against "{ProductName}.{ProductVersion}").
	     managed="yes" native="no": ADO.NET only, no OLE DB provider exists.
	     TODO(ssas-smoke): verify the exact DataSourceProductName the
	     provider reports and that this prefix wins over sql2000.xsl. -->
	<mssqlcrt:provider type="prefix" managed="yes" native="no">ClickHouse</mssqlcrt:provider>
	<mssqlcrt:parameter-style native="unnamed" managed="named"/>

	<mssqlcrt:capabilities>
		<mssqlcrt:supports-datepart-year/>
		<mssqlcrt:supports-datepart-quarter/>
		<mssqlcrt:supports-datepart-month/>
		<mssqlcrt:supports-datepart-dayofyear/>
		<mssqlcrt:supports-datepart-day/>
		<mssqlcrt:supports-datepart-week/>
		<mssqlcrt:supports-datepart-dayofweek/>
		<mssqlcrt:supports-datepart-hour/>
		<mssqlcrt:supports-datepart-minute/>
		<mssqlcrt:supports-datepart-second/>
		<!-- supports-datepart-millisecond omitted: DateTime has second
		     precision; toMillisecond needs DateTime64 -->
		<mssqlcrt:supports-multiple-distinct-count/>
		<!-- supports-update / supports-insert / supports-fast-writeback
		     omitted: import-only provider, no writeback to ClickHouse -->
		<mssqlcrt:supports-subselect/>
		<mssqlcrt:supports-table-alias/>
		<mssqlcrt:supports-column-alias/>
		<mssqlcrt:supports-cast/>
		<!-- supports-remote-query omitted: OPENROWSET is unavailable to
		     managed providers (hence also: one data source per DSV) -->
		<!-- supports-top-clause declared although the dialect has no TOP:
		     the Top template renders LIMIT after ORDER BY, exactly like
		     Microsoft's hive.xsl. TODO(ssas-smoke): confirm SSAS accepts
		     this for ClickHouse processing queries. -->
		<mssqlcrt:supports-top-clause/>
		<mssqlcrt:supports-union/>
		<mssqlcrt:supports-union-all/>
		<!-- supports-materialized-view omitted: ROLAP aggregations are out
		     of scope (import/MOLAP only) -->
		<mssqlcrt:limit-table-identifier-length>64</mssqlcrt:limit-table-identifier-length>
		<mssqlcrt:limit-column-identifier-length>64</mssqlcrt:limit-column-identifier-length>
		<!-- remote-connection-string-mappings omitted together with
		     supports-remote-query -->
	</mssqlcrt:capabilities>

	<!-- Design-time schema helper classes, keyed by the ADO.NET invariant
	     name. Reusing the generic SqlSchema/SqlClientQueryDesigner classes
	     is the pattern proven by the community Npgsql cartridge.
	     ssas-smoke finding (2026-07-20): the assembly here MUST be
	     "Microsoft.DataWarehouse.AS" — that is its name in the VS2022 SSAS
	     extension ("Microsoft.DataWarehouse" was the pre-VS2022 name, and
	     Assembly.Load on it throws FileNotFound, which the DSV wizard
	     swallows, rendering an EMPTY object tree with no error). Verified
	     live: SqlSchema over this provider returns the renamed
	     SchemaName/TableName/TableType shape the wizard expects.
	     SqlClientQueryDesigner is internal in that assembly, so the graphic
	     query designer may not instantiate — non-blocking for the DSV. -->
	<mssqlcrt:schema-classes>
		<mssqlcrt:schema-class>
			<mssqlcrt:managed-provider>Mnemotron.Data.ClickHouse</mssqlcrt:managed-provider>
			<mssqlcrt:type>Microsoft.DataWarehouse.Design.SqlSchema, Microsoft.DataWarehouse.AS</mssqlcrt:type>
			<mssqlcrt:query-designer>
				<mssqlcrt:type>Microsoft.DataWarehouse.Controls.SqlClientQueryDesigner, Microsoft.DataWarehouse.AS</mssqlcrt:type>
			</mssqlcrt:query-designer>
		</mssqlcrt:schema-class>
	</mssqlcrt:schema-classes>

	<!-- Area of internal parametrizations -->
	<!-- overrideOfUseParams:
	         yes      = use always parametric queries
	         no       = never use parametric queries
	         nosubsel = use as yes, but not on subselects
	         auto     = use the value of in_CanUseParams to determine -->
	<!-- Hard "no": literals must be inlined regardless of what the host
	     passes in in_CanUseParams (see header). -->
	<xsl:variable name="overrideOfUseParams">no</xsl:variable>
	<!-- shouldProduceDebug:
	         yes      = produce debug information
	         no       = do not produce debug information -->
	<xsl:variable name="shouldProduceDebug">yes</xsl:variable>

	<!-- Area of global variables initializations -->
	<xsl:variable name="UseParams">
		<xsl:choose>
			<xsl:when test="normalize-space($overrideOfUseParams) = 'yes' or (normalize-space($overrideOfUseParams) = 'auto' and normalize-space($in_CanUseParams)='yes')">yes</xsl:when>
			<xsl:when test="normalize-space($overrideOfUseParams) = 'nosubsel'">nosubsel</xsl:when>
			<xsl:otherwise>no</xsl:otherwise>
		</xsl:choose>
	</xsl:variable>
	<xsl:variable name="ProduceDebug" select="$shouldProduceDebug"/>
	<xsl:variable name="IdentifierStartQuotingCharacter" select="normalize-space($in_IdentStartQuotingCharacter)"/>
	<xsl:variable name="IdentifierEndQuotingCharacter" select="normalize-space($in_IdentEndQuotingCharacter)"/>
	<xsl:variable name="StringStartQuotingCharacter" select="normalize-space($in_StringStartQuotingCharacter)"/>
	<xsl:variable name="StringEndQuotingCharacter" select="normalize-space($in_StringEndQuotingCharacter)"/>

	<!-- Generated statement packaging -->
	<xsl:template match="/">
		<xsl:element name="Statement">

			<!-- Generate query -->
			<xsl:element name="Text">
				<xsl:apply-templates select="./Statement/*[1]"/>
			</xsl:element>

			<!-- Generate parameters -->
			<xsl:if test="count(./Statement/Parameters/Parameter)!=0 and ((normalize-space($UseParams)='yes') or (normalize-space($UseParams)='nosubsel'))">
				<xsl:element name="Parameters">
					<xsl:choose>
						<xsl:when test="/Statement/*[1]//Parameter/@ParamName">
							<xsl:for-each select="./Statement/Parameters/Parameter">
								<xsl:element name="Parameter">
									<xsl:attribute name="ref">
										<xsl:value-of select="./@id"/>
									</xsl:attribute>
								</xsl:element>
							</xsl:for-each>
						</xsl:when>
						<xsl:otherwise>
							<xsl:for-each select="./Statement/*[1]//Parameter">
								<xsl:element name="Parameter">
									<xsl:attribute name="ref">
										<xsl:value-of select="./@ref"/>
									</xsl:attribute>
								</xsl:element>
							</xsl:for-each>
						</xsl:otherwise>
					</xsl:choose>
				</xsl:element>
			</xsl:if>

			<!-- Generate debug info -->
			<xsl:if test="$ProduceDebug='yes'">
				<xsl:element name="mssqldbg:DebugInfo">
					<xsl:element name="mssqldbg:GenerateParametricInfo">
						<xsl:attribute name="mssqldbg:Value">
							<xsl:value-of select="$UseParams"/>
						</xsl:attribute>
						<xsl:element name="mssqldbg:External">
							<xsl:value-of select="$in_CanUseParams"/>
						</xsl:element>
						<xsl:element name="mssqldbg:Internal">
							<xsl:value-of select="$overrideOfUseParams"/>
						</xsl:element>
					</xsl:element>
				</xsl:element>
			</xsl:if>

		</xsl:element>
	</xsl:template>

	<!-- Union statement: bare UNION needs union_default_mode in ClickHouse,
	     so spell out UNION DISTINCT (= SQL UNION semantics) -->
	<xsl:template match="Union">
		<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert'">
			(
		</xsl:if>
			<xsl:call-template name="print-children-list">
				<xsl:with-param name="operator" select="' UNION DISTINCT '"/>
			</xsl:call-template>
		<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert'">
			)
		</xsl:if>
	</xsl:template>

	<!-- Union All statement -->
	<xsl:template match="UnionAll">
		<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert'">
			(
		</xsl:if>
			<xsl:call-template name="print-children-list">
				<xsl:with-param name="operator" select="' UNION ALL '"/>
			</xsl:call-template>
		<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert'">
			)
		</xsl:if>
	</xsl:template>

	<!-- Top clause: rendered as LIMIT and applied after ORDER BY in the
	     Select template (hive.xsl pattern) -->
	<xsl:template match="Top">
		LIMIT <xsl:value-of select="."/><xsl:text> </xsl:text>
	</xsl:template>

	<!-- Select statement -->
	<xsl:template match="Select">
		<xsl:choose>
			<xsl:when test="count(./ColumnExpressions/IsValidForIndexing) != 0">
				<!-- Indexed-view validation probe; only meaningful for ROLAP
				     materialized views, which this cartridge does not declare.
				     Return a constant "not indexable" instead of T-SQL
				     OBJECTPROPERTY. TODO(ssas-smoke): confirm this branch is
				     never reached without supports-materialized-view. -->
				SELECT 0
			</xsl:when>
			<xsl:otherwise>
				<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert' and name(..) != 'Union' and name(..) != 'UnionAll'">
					(
				</xsl:if>
				SELECT <xsl:apply-templates select="./Distinct"/>
					<xsl:apply-templates select="./ColumnExpressions"/>
					<xsl:if test="count(../Phase) != 0">, count() AS <xsl:call-template name="quote-identifier"><xsl:with-param name="identifier" select="'COUNT_BIG_7673aff6-2445-4ef6-a4c9-7bf3d93bd42a'"/></xsl:call-template><xsl:text> </xsl:text></xsl:if>
					<xsl:apply-templates select="./Sources"/>
					<xsl:apply-templates select="./Where"/>
					<xsl:apply-templates select="./GroupBy"/>
					<xsl:apply-templates select="./Having"/>
					<xsl:apply-templates select="./OrderBy"/>
					<xsl:apply-templates select="./Top"/>

					<xsl:if test="name(..) = 'Statement'">
						<xsl:text> </xsl:text><xsl:value-of select="$post-select-query-hint"/>
					</xsl:if>
				<xsl:if test="name(..) != 'Statement' and name(..) != 'Insert' and name(..) != 'Union' and name(..) != 'UnionAll'">
					)
				</xsl:if>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- Update/Insert/Delete/Drop/Create statements: kept for structural
	     completeness. With writeback and materialized-view capabilities
	     omitted, SSAS should not request them against ClickHouse.
	     TODO(ssas-smoke): confirm none of these are emitted during
	     processing. -->
	<xsl:template match="Update">
		<!-- No standard UPDATE in ClickHouse (only ALTER TABLE ... UPDATE);
		     intentionally unsupported. -->
		UPDATE <xsl:apply-templates select="./Target"/>
			<xsl:apply-templates select="./Where"/>
	</xsl:template>

	<xsl:template match="Insert">
		INSERT INTO <xsl:apply-templates select="./Target"/>
		<xsl:apply-templates select="./Select"/>
	</xsl:template>

	<xsl:template match="Delete">
		DELETE FROM <xsl:apply-templates select="./Target"/>
			<xsl:apply-templates select="./Where"/>
	</xsl:template>

	<xsl:template match="Drop">
		DROP <xsl:apply-templates select="./*"/>
	</xsl:template>

	<xsl:template match="Create">
		<!-- ClickHouse CREATE TABLE requires an ENGINE clause the query tree
		     does not carry; intentionally unsupported (writeback only). -->
		CREATE
		<xsl:apply-templates select="./*[1]"/>
		<xsl:choose>
			<xsl:when test="name(./*[1]) = 'Table'"> ( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
		</xsl:choose>
	</xsl:template>

	<!-- RemoteQuery (OPENROWSET) template intentionally omitted:
	     supports-remote-query is not declared. -->

	<xsl:template match="Distinct">
		DISTINCT
	</xsl:template>

	<xsl:template match="As">
		<xsl:apply-templates select="./*[1]"/> AS <xsl:apply-templates select="./*[2]"/>
	</xsl:template>

	<xsl:template match="Sources">
		FROM <xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="ColumnDefinitions">
		<xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="GroupBy">
		GROUP BY <xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="OrderBy">
		ORDER BY <xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="Where">
		WHERE <xsl:apply-templates select="./*"/>
	</xsl:template>

	<xsl:template match="Having">
		HAVING <xsl:apply-templates select="./*"/>
	</xsl:template>

	<xsl:template match="ColumnExpressions">
		<xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="ColumnUpdates">
		<xsl:call-template name="print-children-list"/>
	</xsl:template>

	<xsl:template match="Assign">
		<xsl:apply-templates select="./*[1]"/>=<xsl:apply-templates select="./*[2]"/>
	</xsl:template>

	<xsl:template match="Insert/Target">
		<xsl:apply-templates select="./Table"/>
		(
			<xsl:call-template name="print-list">
				<xsl:with-param name="value-node" select="./ColumnUpdates/*/*[1]"/>
			</xsl:call-template>
		)
		<xsl:if test="./*[2]/Assign">
			VALUES
			(
				<xsl:call-template name="print-list">
					<xsl:with-param name="value-node" select="./ColumnUpdates/*/*[2]"/>
				</xsl:call-template>
			)
		</xsl:if>
	</xsl:template>

	<xsl:template match="Update/Target">
		<xsl:apply-templates select="./Table"/>
		SET <xsl:apply-templates select="./ColumnUpdates"/>
	</xsl:template>

	<xsl:template match="Delete/Target">
		<xsl:apply-templates select="./Table"/>
	</xsl:template>

	<xsl:template match="Insert/Target/ColumnUpdates/Assign/SQLColumn[1]">
		<!-- The table name is not printed for LHS of assignments in insert -->
		<xsl:apply-templates select="./Column"/>
	</xsl:template>

	<xsl:template match="Create/ColumnDefinitions/SQLColumn">
		<xsl:apply-templates select="./Column"/>
		<xsl:if test="count(../../Phase) = 0">
			<xsl:apply-templates select="./Type"/>
			<xsl:apply-templates select="./Usage"/>
		</xsl:if>
	</xsl:template>

	<xsl:template match="SQLColumn">
		<xsl:variable name="table">
			<xsl:if test="name(..)!='Count' or name(./Column/*[1]) != 'Asterisk'"><xsl:apply-templates select="./Table"/></xsl:if>
		</xsl:variable>
		<xsl:variable name="column">
			<xsl:apply-templates select="./Column"/>
		</xsl:variable>
		<xsl:choose>
			<xsl:when test="$table = ''">
				<xsl:value-of select="$column"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="concat($table,'.', $column)"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<xsl:template match="Create/Database|Drop/Database">
		DATABASE <xsl:apply-templates select="./Name"/>
	</xsl:template>

	<xsl:template match="Table">
		<xsl:call-template name="build-quoted-schema-object"/>
	</xsl:template>

	<xsl:template match="Create/Table|Drop/Table">
		TABLE <xsl:call-template name="build-quoted-schema-object"/>
	</xsl:template>

	<xsl:template match="View">
		<xsl:call-template name="build-quoted-schema-object"/>
	</xsl:template>

	<xsl:template match="Drop/View">
		VIEW <xsl:call-template name="build-quoted-schema-object"/>
	</xsl:template>

	<xsl:template match="Index">
		<xsl:apply-templates select="./Name"/>
	</xsl:template>

	<xsl:template match="Drop/Index">
		INDEX <xsl:apply-templates select="./Name"/>
	</xsl:template>

	<xsl:template match="Column">
		<xsl:apply-templates select="./Asterisk"/>
		<xsl:apply-templates select="./Name"/>
	</xsl:template>

	<!-- Schema maps to the ClickHouse database: "db"."table" -->
	<xsl:template match="Table/Name|View/Name|Index/Name|Column/Name|Database/Name|Schema">
		<xsl:call-template name="quote-identifier"/>
	</xsl:template>

	<xsl:template match="Usage">
		<!-- Column-level "primary key" is a table-engine concern in
		     ClickHouse; emit nothing (Create is unsupported anyway). -->
	</xsl:template>

	<xsl:template match="OpaqueExpression">
		<xsl:if test="name(..) = 'As' and name(../..) = 'Sources'">
			(
		</xsl:if>
		<xsl:value-of select="."/>
		<xsl:if test="name(..) = 'As' and name(../..) = 'Sources'">
			)
		</xsl:if>
	</xsl:template>

	<xsl:template match="OrderExpression">
		<xsl:apply-templates select="./*[1]"/>
		<xsl:apply-templates select="./Asc"/>
		<xsl:apply-templates select="./Desc"/>
	</xsl:template>

	<xsl:template match="Asc">
		ASC
	</xsl:template>

	<xsl:template match="Desc">
		DESC
	</xsl:template>

	<!-- Print values: ? for parametric queries and convert expressions
	     for non-parametric queries. UseParams is hard-disabled, so the
	     non-parametric branch is always taken (see header). -->
	<xsl:template match="Parameter">
		<xsl:choose>
			<xsl:when test="$UseParams = 'yes' or $UseParams = 'nosubsel'">
				<xsl:choose>
					<xsl:when test="./@ParamName">
						@<xsl:value-of select="./@ParamName"/>
					</xsl:when>
					<xsl:otherwise>
						?
					</xsl:otherwise>
				</xsl:choose>
			</xsl:when>
			<xsl:otherwise>
				<xsl:call-template name="print-non-parametric-parameter-ref">
					<xsl:with-param name="parameter-reference" select="."/>
				</xsl:call-template>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- DatePart: ClickHouse has no DATEPART(part, x); dispatch to the
	     to*() function family instead. -->
	<xsl:template match="DatePart">
		<xsl:variable name="dps-val"><xsl:value-of select="normalize-space(./*[1]/text())"/></xsl:variable>
		<xsl:choose>
			<xsl:when test="$dps-val = 'Year'">toYear( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Quarter'">toQuarter( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Month'">toMonth( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'DayOfYear'">toDayOfYear( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Day'">toDayOfMonth( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<!-- TODO(ssas-smoke): T-SQL week/weekday numbering differs from
			     ClickHouse defaults (toWeek mode 0; toDayOfWeek is
			     1 = Monday, T-SQL default weekday is 1 = Sunday). Verify
			     whether SSAS relies on the exact numbering. -->
			<xsl:when test="$dps-val = 'Week'">toWeek( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'DayOfWeek'">toDayOfWeek( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Hour'">toHour( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Minute'">toMinute( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
			<xsl:when test="$dps-val = 'Second'">toSecond( <xsl:apply-templates select="./*[2]"/> )</xsl:when>
		</xsl:choose>
	</xsl:template>

	<!-- Asterisk -->
	<xsl:template match="Asterisk">*</xsl:template>

	<!-- Count/Min/Max/Sum -->
	<xsl:template match="Min|Max|Sum">
		<xsl:variable name="function">
			<xsl:choose>
				<xsl:when test="name()='Min'"> MIN </xsl:when>
				<xsl:when test="name()='Max'"> MAX </xsl:when>
				<xsl:when test="name()='Sum'"> SUM </xsl:when>
			</xsl:choose>
		</xsl:variable>
		<xsl:value-of select="$function"/>( <xsl:apply-templates select="./*[1]"/> )
	</xsl:template>

	<!-- COUNT_BIG is T-SQL; ClickHouse count() already returns UInt64 -->
	<xsl:template match="Count">
		count( <xsl:apply-templates select="./*[1]"/> <xsl:apply-templates select="./*[2]"/> )
	</xsl:template>

	<!-- Binary expressions -->
	<xsl:template match="Equal|NotEqual|Greater|GreaterOrEqual|Less|LessOrEqual|In|And|Or|Plus|Minus|Divide|Multiply">
		<xsl:variable name="operator">
			<xsl:choose>
				<xsl:when test="name()='Equal'">			=		</xsl:when>
				<xsl:when test="name()='NotEqual'">			&lt;&gt;	</xsl:when>
				<xsl:when test="name()='Greater'">			&gt;		</xsl:when>
				<xsl:when test="name()='GreaterOrEqual'"><![CDATA[	>=		]]></xsl:when>
				<xsl:when test="name()='Less'">				&lt;	</xsl:when>
				<xsl:when test="name()='LessOrEqual'">		&lt;=	</xsl:when>
				<xsl:when test="name()='In'">				IN		</xsl:when>
				<xsl:when test="name()='And'">				AND		</xsl:when>
				<xsl:when test="name()='Or'">				OR		</xsl:when>
				<xsl:when test="name()='Plus'">				+		</xsl:when>
				<xsl:when test="name()='Minus'">			-		</xsl:when>
				<xsl:when test="name()='Divide'">			/		</xsl:when>
				<xsl:when test="name()='Multiply'">			*		</xsl:when>
			</xsl:choose>
		</xsl:variable>
		(
			<xsl:call-template name="print-children-list">
				<xsl:with-param name="operator" select="$operator"/>
			</xsl:call-template>
		)
	</xsl:template>

	<!-- Postfix unary expressions -->
	<xsl:template match="IsNull">
		<xsl:variable name="operator">
			<xsl:choose>
				<xsl:when test="name()='IsNull'">			IS NULL		</xsl:when>
			</xsl:choose>
		</xsl:variable>
		(
			<xsl:apply-templates select="./*[1]"/>
			<xsl:value-of select="$operator"/>
		)
	</xsl:template>

	<!-- True and False (ClickHouse accepts the keywords) -->
	<xsl:template match="True">
		TRUE
	</xsl:template>

	<xsl:template match="False">
		FALSE
	</xsl:template>

	<!-- Types: OLE DB DBTYPE_* to ClickHouse type names for CAST.
	     String carries no length (no (n) suffix); Decimal keeps (P, S).
	     TODO(ssas-smoke): verify which DBTYPE_* values actually reach the
	     cartridge from DSV named queries/calculated columns. -->
	<xsl:template match="Type">
		<xsl:variable name="type-val"><xsl:value-of select="normalize-space(./text())"/></xsl:variable>
		<xsl:choose>
			<xsl:when test="$type-val = 'DBTYPE_BSTR'">String</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_WSTR'">String</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_STR'">String</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_BOOL'">UInt8</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_I1'">Int8</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_I2'">Int16</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_I4'">Int32</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_I8'">Int64</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_UI1'">UInt8</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_UI2'">UInt16</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_UI4'">UInt32</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_UI8'">UInt64</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_R4'">Float32</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_R8'">Float64</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_DBDATE'">Date</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_DATE'">DateTime</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_DBTIMESTAMP'">DateTime</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_CY'">Decimal(18, 4)</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_VARIANT'">String</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_GUID'">UUID</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_ByteArray'">String</xsl:when>
			<xsl:when test="$type-val = 'DBTYPE_DECIMAL' or $type-val = 'DBTYPE_NUMERIC'">Decimal<xsl:if test="count(./Precision) != 0">(<xsl:value-of select="normalize-space(./Precision/text())"/><xsl:if test="count(./Scale) != 0">, <xsl:value-of select="normalize-space(./Scale/text())"/></xsl:if>)</xsl:if></xsl:when>
		</xsl:choose>
	</xsl:template>

	<!-- Cast -->
	<xsl:template match="Cast">
		CAST(<xsl:apply-templates select="./*[1]"/> AS <xsl:apply-templates select="./*[2]"/>)
	</xsl:template>

	<!-- By default don't do anything -->
	<xsl:template match="*">
	</xsl:template>

	<!-- Print a schema object -->
	<xsl:template name="build-quoted-schema-object">
		<xsl:param name="schema-object-node" select="."/>

		<xsl:variable name="unquoted-schema">
			<xsl:value-of select="$schema-object-node/Schema"/>
		</xsl:variable>
		<xsl:variable name="schema">
			<xsl:apply-templates select="$schema-object-node/Schema"/>
		</xsl:variable>
		<xsl:variable name="table">
			<xsl:apply-templates select="$schema-object-node/Name"/>
		</xsl:variable>
		<xsl:choose>
			<xsl:when test="$unquoted-schema = ''">
				<xsl:value-of select="$table"/>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="concat($schema,'.', $table)"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- Convert an identifier to the quotation form -->
	<xsl:template name="quote-identifier">
		<xsl:param name="identifier" select="."/>

		<xsl:value-of select="$IdentifierStartQuotingCharacter"/>
		<xsl:call-template name="normalize-entity-aux">
			<xsl:with-param name="entity" select="$identifier"/>
			<xsl:with-param name="end-quoting-char" select="$IdentifierEndQuotingCharacter"/>
		</xsl:call-template>
		<xsl:value-of select="$IdentifierEndQuotingCharacter"/>
	</xsl:template>

	<!-- Convert a string to the string quotation form -->
	<xsl:template name="quote-string">
		<xsl:param name="string" select="."/>

		<xsl:value-of select="$StringStartQuotingCharacter"/>
		<xsl:call-template name="normalize-entity-aux">
			<xsl:with-param name="entity" select="$string"/>
			<xsl:with-param name="end-quoting-char" select="$StringEndQuotingCharacter"/>
		</xsl:call-template>
		<xsl:value-of select="$StringEndQuotingCharacter"/>
	</xsl:template>

	<!-- Convert an entity to the quotation form (recursive, aux) by
	     duplicating the end quoting character. Doubling is valid ClickHouse
	     escaping for both '' in strings and "" in identifiers. -->
	<xsl:template name="normalize-entity-aux">
		<xsl:param name="entity"/>
		<xsl:param name="end-quoting-char"/>

		<xsl:choose>
			<xsl:when test="contains($entity, $end-quoting-char)">
				<xsl:value-of select="substring-before($entity, $end-quoting-char)"/>
				<xsl:value-of select="$end-quoting-char"/>
				<xsl:value-of select="$end-quoting-char"/>
				<xsl:call-template name="normalize-entity-aux">
					<xsl:with-param name="entity" select="substring-after($entity, $end-quoting-char)"/>
					<xsl:with-param name="end-quoting-char" select="$end-quoting-char"/>
				</xsl:call-template>
			</xsl:when>
			<xsl:otherwise>
				<xsl:value-of select="$entity"/>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>

	<!-- Print the children list -->
	<xsl:template name="print-children-list">
		<xsl:param name="operator" select="','"/>

		<xsl:call-template name="print-list">
			<xsl:with-param name="value-node" select="./*"/>
			<xsl:with-param name="operator" select="$operator"/>
		</xsl:call-template>
	</xsl:template>

	<!-- Print a list -->
	<xsl:template name="print-list">
		<xsl:param name="operator" select="','"/>
		<xsl:param name="value-node" select="."/>

		<xsl:for-each select="$value-node">
			<xsl:apply-templates select="."/>
			<xsl:if test="position()!=last()">
				<xsl:value-of select="$operator"/>
			</xsl:if>
		</xsl:for-each>
	</xsl:template>

	<!-- Get the value of a parameter ref for the query -->
	<xsl:template name="print-non-parametric-parameter-ref">
		<xsl:param name="parameter-reference"/>

		<xsl:variable name="reference"><xsl:value-of select="$parameter-reference/@ref"/></xsl:variable>
		<xsl:call-template name="print-non-parametric-parameter">
			<xsl:with-param name="parameter" select="/Statement/Parameters/Parameter[@id=$reference]"/>
		</xsl:call-template>
	</xsl:template>

	<!-- Inline a parameter value as a ClickHouse literal.
	     TODO(ssas-smoke): verify the textual format SSAS puts into
	     parameter values (especially date/time and decimal separators)
	     parses on the ClickHouse side; switch the datetime branches to
	     parseDateTimeBestEffort if CAST proves too strict. -->
	<xsl:template name="print-non-parametric-parameter">
		<xsl:param name="parameter"/>

		<xsl:variable name="db-type"><xsl:value-of select="$parameter/@DBTYPE"/></xsl:variable>
		<xsl:variable name="value"><xsl:value-of select="$parameter/text()"/></xsl:variable>
		<xsl:choose>
			<xsl:when test="$db-type = 'DBTYPE_BSTR'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_WSTR'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_STR'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_BOOL'">CAST(<xsl:value-of select="$value"/> AS UInt8)</xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_I1'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_I2'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_I4'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_I8'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_UI1'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_UI2'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_UI4'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_UI8'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_R4'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_R8'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_DBDATE'">CAST( <xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template> AS Date)</xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_DATE'">CAST( <xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template> AS DateTime)</xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_DBTIMESTAMP'">CAST( <xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template> AS DateTime)</xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_CY'">CAST( <xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template> AS Decimal(18, 4))</xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_VARIANT'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_GUID'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_ByteArray'"><xsl:call-template name="quote-string"><xsl:with-param name="string" select="$value"/></xsl:call-template></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_DECIMAL'"><xsl:value-of select="$value"/></xsl:when>
			<xsl:when test="$db-type = 'DBTYPE_EMPTY'">NULL</xsl:when>
		</xsl:choose>
	</xsl:template>
</xsl:stylesheet>
