#======================================================================================================================
#   /---------------------------------------------------\
#   | Thank you for selecting Selman & Associates, Ltd. |
#   |                 www.SelmanLog.com                 |
#   \---------------------------------------------------/
#======================================================================================================================
~Version
 VERS   .          3.0                                : CWLS Log ASCII Standard(LAS) file - Version 3.0
 WRAP   .          No                                 : One line per depth step
 DLM    .          Comma                              : Comma used as delimiting character
#======================================================================================================================
~Well 
#MNEM   .UNIT      Value                              Description
#----------------------------------------------------------------------------------------------------------------------
 STRT   .Ft        5936                               : Start Depth
 STOP   .Ft        14500                              : Stop Depth 
 STEP   .Ft        1                                  : Step Length 
 NULL   .          -999.25                            : NULL Value
 COMP   .          CONOCOPHILLIPS COMPANY             : Company
 WELL   .          UNIVERSITY ANDREWS 3001HL          : Well
 FLD    .          EMBAR (PERMIAN)                    : Field        
 LOC    .          2168' FNL & 262' FWL, SEC 30, BLK 10, UL SURVEY, A-U314: Location
 CNTY   .          ANDREWS                            : County
 STAT   .          TX                                 : State
 CTRY   .          US                                 : Country
 API    .          42-003-46370                       : API Reference Number
 SRVC   .          SELMAN & ASSOCIATES, LTD.          : Service Company
 LATI   .                                             : Lattitude
 LONG   .                                             : Longitude
 GDAT   .                                             : Geodetic Datum
 DATE   .          2/9/2015                           : Service Date  {M/D/YYYY}
 TILL   .          2/23/2015                          : Release Date
#======================================================================================================================
~Parameter    
#MNEM   .UNIT      Value                              Description
#----------------------------------------------------------------------------------------------------------------------
 UNIT   .          72                        : Selman Logging Unit Number
 EKB    .          3267.7                    : Kelly Bushing Elevation
 EGL    .          3250.2                    : Ground-level Elevation
 SELWN  .          8959LAT                   : Selman Well Invoice Number
#======================================================================================================================
~Curve  
#MNEM   .UNIT      API Code          Curve#  Description
#----------------------------------------------------------------------------------------------------------------------
 DEPTH  .Ft                          :       Depth  {F}
 GAS    .Units     91 830 01 00      :1      TOTAL GAS
 C1     .Units     91 831 01 00      :2      C1 - METHANE
 C2     .Units     91 832 01 00      :3      C2 - ETHANE
 C3     .Units     91 833 01 00      :4      C3 - PROPANE
 C4     .Units     96 182 01 00      :5      C4 - BUTANE
 CO2    .%         91 841 01 00      :6      CARBON DIOXIDE [%]
 H2S    .ppm       91 842 01 00      :7      HYDROGEN SULFIDE [PPM]
 DR     .          96 180 01 00      :8      DRILL RATE [MIN/FT]
 WOB    .          90 811 03 00      :9      WOB [kLBS]
 GAMMA  .          35 310 30 00      :10     GAMMA RAY [GAPI]
 ANHY   .%         94 851 21 00      :11     ANHYDRITE
 BENTNTE.%         94 851 83 01      :12     BENTONITE
 CALCITE.%         94 851 83 02      :13     CALCITE
 CHERT  .%         94 851 04 00      :14     CHERT
 COAL   .%         94 851 31 00      :15     COAL
 CONG   .%         94 851 05 00      :16     CONGLOMERATE
 DOLOS  .%         94 851 11 00      :17     DOLOMITE
 GRAN   .%         94 851 80 00      :18     GRANITE
 GWASH  .%         94 851 81 00      :19     GRANITE WASH
 LIMES  .%         94 851 10 00      :20     LIMESTONE
 SALT   .%         94 851 22 00      :21     SALT
 SHALE  .%         94 861 02 00      :22     SHALE
 SILTS  .%         94 850 02 00      :23     SILTSTONE
 SANDS  .%         94 850 01 00      :24     SAND
 CEMENT .%         94 851 83 03      :25     CEMENT
 GFLUOR .%         00 000 00 00      :26     Good fluorescence
 FFLUOR .%         00 000 00 00      :27     Fair fluorescence
 TFLUOR .%         00 000 00 00      :28     Trace fluorescence
 FLUOR  .%         94 930 03 00      :29     Total Fluorescence
 FLUORT .          00 000 00 00      :30     Type of Fluorescence: (NONE=0, TRACE=1, FAIR=2, GOOD=3)
 TRCUT  .          00 000 00 00      :31     Trace cut
 FRCUT  .          00 000 00 00      :32     Fair cut
 GDCUT  .          00 000 00 00      :33     Good cut
 CUTTYP .          94 930 83 00      :34     Type of Cut: (NONE=0, TRCUT=1, FRCUT=2, GDCUT=3)
 TRPOR  .          94 930 81 02      :35     Trace porosity
 FRPOR  .          94 930 81 03      :36     Fair porosity
 GDPOR  .          94 930 81 04      :37     Good porosity
 PORTYP .          94 891 80 00      :38     Type of Porosity: (NONE=0, TRACE=1, FAIR=2, GOOD=3)
 SLIDE  .          00 000 00 00      :39     Whether Sliding or Rotating (1=SLIDE, 0=ROTATE) 
#======================================================================================================================         
#Depth     GAS      C1      C2      C3      C4     CO2     H2S      DR     WOB   GAMMA ANHY BENTNTE CALCITE CHERT COAL CONG DOLOS GRAN GWASH LIMES SALT SHALE SILTS SANDS CEMENT GFLUOR FFLUOR TFLUOR FLUOR FLUORT TRCUT FRCUT GDCUT CUTTYP TRPOR FRPOR GDPOR PORTYP SLIDE
~Ascii
5936  , 381.12,  190.7,  116.3,   23.5,      0,    0.1,   0.52,   0.89,     19,   42.6,   0,      0,      0,    0,   0,   0,  100,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,    20,   20,     1,    1,    0,    0,     1,    0,    0,    0,     0,    0
5937  , 711.52,  469.6,      0,      0,      0,   0.11,   0.49,   0.61,  154.1,  68.16,   0,      0,      0,    0,   0,   0,  100,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,    20,   20,     1,    1,    0,    0,     1,    0,    0,    0,     0,    0
5938  ,   1.35,      0,      0,      0,      0,   0.14,   6.11,   2.49,   10.3,   63.9,   0,      0,      0,    0,   0,   0,  100,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,    20,   20,     1,    1,    0,    0,     1,    0,    0,    0,     0,    0
14498 ,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,   0,      0,      0,    0,   0,   0,    0,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,     0,    0,     0,    0,    0,    0,     0,    0,    0,    0,     0,    0
14499 ,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,   0,      0,      0,    0,   0,   0,    0,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,     0,    0,     0,    0,    0,    0,     0,    0,    0,    0,     0,    0
14500 ,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,-999.25,   0,      0,      0,    0,   0,   0,    0,   0,    0,    0,   0,    0,    0,    0,     0,     0,     0,     0,    0,     0,    0,    0,    0,     0,    0,    0,    0,     0,    0
#======================================================================================================================

~Lithology_Definition
#MNEM   .UNIT      API Code            Description                Format
#----------------------------------------------------------------------------------------------------------------------
 FROM   .Ft                          : From Depth
 TO     .Ft                          : To Depth
 LITH   .                            : Lithology Description      {S}
#======================================================================================================================


#FROM TO    LITH                                                                                                                                                                                                      
~Lithology_Data | Lithology_Definition
5936 ,5980 ,BEGIN TWO MAN LOGGING /*OPERATIONS AT 2200 HRS/*ON 2/9/2015 @ A DEPTH OF 5936'/*BIT #3  8.75" BAKER HUGHES DPD506X/*LAST  SURVEY @ 5874' INC. 7.8 AZM. 236.8/*MUD WT 8.4 VIS 37 PH 10                    
5980 ,6020 ,DOLOMITE: PRED OFF WH - TN - WH - LTGY MICROXLN CALC FRMWRK NO FLUOR NO CUT NO RNG                                                                                                                       
6020 ,6050 ,DOLOMITE: PRED OFF WH - TN - WH - LTGY MICROXLN CALC FRMWRK NO FLUOR NO CUT NO RNG                                                                                                                       
