#include "Common.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>

static int g_maxLevel = 50;
static int g_maxLevelInclusive = 51;

static void LoadOptionsFromSidecar(HINSTANCE hinstDLL)
{
    char modulePath[MAX_PATH] = {};
    if (GetModuleFileNameA(hinstDLL, modulePath, MAX_PATH) == 0)
        return;

    // options.ini sits beside the patch DLL: patches/<id>/options.ini
    char* lastSlash = strrchr(modulePath, '\\');
    if (!lastSlash)
        lastSlash = strrchr(modulePath, '/');
    if (!lastSlash)
        return;

    *(lastSlash + 1) = '\0';
    char optionsPath[MAX_PATH];
    snprintf(optionsPath, MAX_PATH, "%soptions.ini", modulePath);

    FILE* f = fopen(optionsPath, "r");
    if (!f)
        return;

    char line[128];
    bool sawInclusive = false;
    while (fgets(line, sizeof(line), f))
    {
        char key[64] = {};
        int value = 0;
        if (sscanf(line, "%63[^=]=%d", key, &value) != 2)
            continue;

        if (strcmp(key, "max_level") == 0)
        {
            g_maxLevel = value;
            debugLog("[LevelUpLimit] options.ini max_level=%d", g_maxLevel);
        }
        else if (strcmp(key, "max_level_inclusive") == 0)
        {
            g_maxLevelInclusive = value;
            sawInclusive = true;
            debugLog("[LevelUpLimit] options.ini max_level_inclusive=%d", g_maxLevelInclusive);
        }
    }
    fclose(f);

    if (!sawInclusive)
        g_maxLevelInclusive = g_maxLevel + 1;
}

extern "C" void __cdecl InitRequiredExpPerLevel(void* rules)
{
    debugLog("[LevelUpLimit] Running InitRequiredExpPerLevel\nRules: %X", rules);
    int* requiredExpPerLevel = new int[g_maxLevelInclusive];
    setObjectProperty<int*>(rules, 0x38, requiredExpPerLevel); //required_exp_per_level
    debugLog("[LevelUpLimit] Finished InitRequiredExpPerLevel\nrequiredExpPerLevel: %X", requiredExpPerLevel);

}

extern "C" void __cdecl DisposeRequiredExpPerLevel(void* rules)
{
    debugLog("[LevelUpLimit] Running DisposeRequiredExpPerLevel");
    int* requiredExpPerLevel = getObjectProperty<int*>(rules, 0x38);
    if (requiredExpPerLevel) {
        delete[] requiredExpPerLevel;
    }
}

extern "C" void __cdecl InitNumSpellLevels(void* thisClass)
{
    debugLog("[LevelUpLimit] Running InitNumSpellLevels");
    setObjectProperty<BYTE*>(thisClass, 0x114, new BYTE[g_maxLevel]); //level_num_spell_levels
}

extern "C" void __cdecl InitPowerGain(void* thisClass)
{
    debugLog("[LevelUpLimit] Running InitPowerGain");
    BYTE* powerGain = new BYTE[g_maxLevel];
    memset(powerGain, 0xff, g_maxLevel);
    setObjectProperty<BYTE*>(thisClass, 0x128, powerGain); //level_power_gain
}

extern "C" void __cdecl InitOtherClassTables(void* thisClass)
{
    debugLog("[LevelUpLimit] Running InitOtherClassTables");
    setObjectProperty<BYTE*>(thisClass, 0x13c, new BYTE[g_maxLevel]); //level_bonus_feat_gains
    setObjectProperty<BYTE*>(thisClass, 0x150, new BYTE[g_maxLevel]); //level_feat_gains
    setObjectProperty<BYTE*>(thisClass, 0x184, new BYTE[g_maxLevel]); //level_effective_cr
}

extern "C" void __cdecl DisposeClassTables(void* thisClass)
{
    debugLog("[LevelUpLimit] Running DisposeClassTables");

    BYTE* numSpellLevels = getObjectProperty<BYTE*>(thisClass, 0x114);
    if (numSpellLevels) {
        delete[] numSpellLevels;
    }
    
    BYTE* levelPowerGain = getObjectProperty<BYTE*>(thisClass, 0x128);
    if (levelPowerGain) {
        delete[] levelPowerGain;
    }

    BYTE* levelBonusFeatGains = getObjectProperty<BYTE*>(thisClass, 0x13c);
    if (levelBonusFeatGains) {
        delete[] levelBonusFeatGains;
    }

    BYTE* levelFeatGains = getObjectProperty<BYTE*>(thisClass, 0x150);
    if (levelFeatGains) {
        delete[] levelFeatGains;
    }

    BYTE* levelEffectiveCR = getObjectProperty<BYTE*>(thisClass, 0x184);
    if (levelEffectiveCR) {
        delete[] levelEffectiveCR;
    }
}

// DLL Entry Point
BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    switch (fdwReason)
    {
    case DLL_PROCESS_ATTACH:
        LoadOptionsFromSidecar(hinstDLL);
        break;

    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
