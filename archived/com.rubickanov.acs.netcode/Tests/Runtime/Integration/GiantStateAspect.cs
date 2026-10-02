using R3;
using Rubickanov.ACS.Runtime;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests.Integration
{
    // ---- Giant aspect (regression #3) ---------------------------------------
    //
    // 257 [Replicated] ReactiveProperty<int> fields — exactly one over the
    // 256-field cap. Used to drive the ExceedsFieldBindingCap abort path in
    // EntityReplicator.OnNetworkSpawn through a real spawn — the predicate
    // itself is unit-tested separately, but the integration wiring ("hit the
    // cap at spawn time and _system stays null / registration never happens")
    // is only observable end-to-end.
    //
    // Why 257 (not 300 or 1000): the point of the test is to verify the
    // guard fires at the boundary. More fields would just slow the test down.
    //
    // Field naming is F000..F256 so the alphabetical sort in ReplicationScanner
    // is trivially stable and byte-index boundary cases (#255 → byte 255,
    // #256 → would-be byte 0) line up with declaration order.

    public sealed class GiantStateAspect : IEntityAspect
    {
        [Replicated] public ReactiveProperty<int> F000 = new(0);
        [Replicated] public ReactiveProperty<int> F001 = new(0);
        [Replicated] public ReactiveProperty<int> F002 = new(0);
        [Replicated] public ReactiveProperty<int> F003 = new(0);
        [Replicated] public ReactiveProperty<int> F004 = new(0);
        [Replicated] public ReactiveProperty<int> F005 = new(0);
        [Replicated] public ReactiveProperty<int> F006 = new(0);
        [Replicated] public ReactiveProperty<int> F007 = new(0);
        [Replicated] public ReactiveProperty<int> F008 = new(0);
        [Replicated] public ReactiveProperty<int> F009 = new(0);
        [Replicated] public ReactiveProperty<int> F010 = new(0);
        [Replicated] public ReactiveProperty<int> F011 = new(0);
        [Replicated] public ReactiveProperty<int> F012 = new(0);
        [Replicated] public ReactiveProperty<int> F013 = new(0);
        [Replicated] public ReactiveProperty<int> F014 = new(0);
        [Replicated] public ReactiveProperty<int> F015 = new(0);
        [Replicated] public ReactiveProperty<int> F016 = new(0);
        [Replicated] public ReactiveProperty<int> F017 = new(0);
        [Replicated] public ReactiveProperty<int> F018 = new(0);
        [Replicated] public ReactiveProperty<int> F019 = new(0);
        [Replicated] public ReactiveProperty<int> F020 = new(0);
        [Replicated] public ReactiveProperty<int> F021 = new(0);
        [Replicated] public ReactiveProperty<int> F022 = new(0);
        [Replicated] public ReactiveProperty<int> F023 = new(0);
        [Replicated] public ReactiveProperty<int> F024 = new(0);
        [Replicated] public ReactiveProperty<int> F025 = new(0);
        [Replicated] public ReactiveProperty<int> F026 = new(0);
        [Replicated] public ReactiveProperty<int> F027 = new(0);
        [Replicated] public ReactiveProperty<int> F028 = new(0);
        [Replicated] public ReactiveProperty<int> F029 = new(0);
        [Replicated] public ReactiveProperty<int> F030 = new(0);
        [Replicated] public ReactiveProperty<int> F031 = new(0);
        [Replicated] public ReactiveProperty<int> F032 = new(0);
        [Replicated] public ReactiveProperty<int> F033 = new(0);
        [Replicated] public ReactiveProperty<int> F034 = new(0);
        [Replicated] public ReactiveProperty<int> F035 = new(0);
        [Replicated] public ReactiveProperty<int> F036 = new(0);
        [Replicated] public ReactiveProperty<int> F037 = new(0);
        [Replicated] public ReactiveProperty<int> F038 = new(0);
        [Replicated] public ReactiveProperty<int> F039 = new(0);
        [Replicated] public ReactiveProperty<int> F040 = new(0);
        [Replicated] public ReactiveProperty<int> F041 = new(0);
        [Replicated] public ReactiveProperty<int> F042 = new(0);
        [Replicated] public ReactiveProperty<int> F043 = new(0);
        [Replicated] public ReactiveProperty<int> F044 = new(0);
        [Replicated] public ReactiveProperty<int> F045 = new(0);
        [Replicated] public ReactiveProperty<int> F046 = new(0);
        [Replicated] public ReactiveProperty<int> F047 = new(0);
        [Replicated] public ReactiveProperty<int> F048 = new(0);
        [Replicated] public ReactiveProperty<int> F049 = new(0);
        [Replicated] public ReactiveProperty<int> F050 = new(0);
        [Replicated] public ReactiveProperty<int> F051 = new(0);
        [Replicated] public ReactiveProperty<int> F052 = new(0);
        [Replicated] public ReactiveProperty<int> F053 = new(0);
        [Replicated] public ReactiveProperty<int> F054 = new(0);
        [Replicated] public ReactiveProperty<int> F055 = new(0);
        [Replicated] public ReactiveProperty<int> F056 = new(0);
        [Replicated] public ReactiveProperty<int> F057 = new(0);
        [Replicated] public ReactiveProperty<int> F058 = new(0);
        [Replicated] public ReactiveProperty<int> F059 = new(0);
        [Replicated] public ReactiveProperty<int> F060 = new(0);
        [Replicated] public ReactiveProperty<int> F061 = new(0);
        [Replicated] public ReactiveProperty<int> F062 = new(0);
        [Replicated] public ReactiveProperty<int> F063 = new(0);
        [Replicated] public ReactiveProperty<int> F064 = new(0);
        [Replicated] public ReactiveProperty<int> F065 = new(0);
        [Replicated] public ReactiveProperty<int> F066 = new(0);
        [Replicated] public ReactiveProperty<int> F067 = new(0);
        [Replicated] public ReactiveProperty<int> F068 = new(0);
        [Replicated] public ReactiveProperty<int> F069 = new(0);
        [Replicated] public ReactiveProperty<int> F070 = new(0);
        [Replicated] public ReactiveProperty<int> F071 = new(0);
        [Replicated] public ReactiveProperty<int> F072 = new(0);
        [Replicated] public ReactiveProperty<int> F073 = new(0);
        [Replicated] public ReactiveProperty<int> F074 = new(0);
        [Replicated] public ReactiveProperty<int> F075 = new(0);
        [Replicated] public ReactiveProperty<int> F076 = new(0);
        [Replicated] public ReactiveProperty<int> F077 = new(0);
        [Replicated] public ReactiveProperty<int> F078 = new(0);
        [Replicated] public ReactiveProperty<int> F079 = new(0);
        [Replicated] public ReactiveProperty<int> F080 = new(0);
        [Replicated] public ReactiveProperty<int> F081 = new(0);
        [Replicated] public ReactiveProperty<int> F082 = new(0);
        [Replicated] public ReactiveProperty<int> F083 = new(0);
        [Replicated] public ReactiveProperty<int> F084 = new(0);
        [Replicated] public ReactiveProperty<int> F085 = new(0);
        [Replicated] public ReactiveProperty<int> F086 = new(0);
        [Replicated] public ReactiveProperty<int> F087 = new(0);
        [Replicated] public ReactiveProperty<int> F088 = new(0);
        [Replicated] public ReactiveProperty<int> F089 = new(0);
        [Replicated] public ReactiveProperty<int> F090 = new(0);
        [Replicated] public ReactiveProperty<int> F091 = new(0);
        [Replicated] public ReactiveProperty<int> F092 = new(0);
        [Replicated] public ReactiveProperty<int> F093 = new(0);
        [Replicated] public ReactiveProperty<int> F094 = new(0);
        [Replicated] public ReactiveProperty<int> F095 = new(0);
        [Replicated] public ReactiveProperty<int> F096 = new(0);
        [Replicated] public ReactiveProperty<int> F097 = new(0);
        [Replicated] public ReactiveProperty<int> F098 = new(0);
        [Replicated] public ReactiveProperty<int> F099 = new(0);
        [Replicated] public ReactiveProperty<int> F100 = new(0);
        [Replicated] public ReactiveProperty<int> F101 = new(0);
        [Replicated] public ReactiveProperty<int> F102 = new(0);
        [Replicated] public ReactiveProperty<int> F103 = new(0);
        [Replicated] public ReactiveProperty<int> F104 = new(0);
        [Replicated] public ReactiveProperty<int> F105 = new(0);
        [Replicated] public ReactiveProperty<int> F106 = new(0);
        [Replicated] public ReactiveProperty<int> F107 = new(0);
        [Replicated] public ReactiveProperty<int> F108 = new(0);
        [Replicated] public ReactiveProperty<int> F109 = new(0);
        [Replicated] public ReactiveProperty<int> F110 = new(0);
        [Replicated] public ReactiveProperty<int> F111 = new(0);
        [Replicated] public ReactiveProperty<int> F112 = new(0);
        [Replicated] public ReactiveProperty<int> F113 = new(0);
        [Replicated] public ReactiveProperty<int> F114 = new(0);
        [Replicated] public ReactiveProperty<int> F115 = new(0);
        [Replicated] public ReactiveProperty<int> F116 = new(0);
        [Replicated] public ReactiveProperty<int> F117 = new(0);
        [Replicated] public ReactiveProperty<int> F118 = new(0);
        [Replicated] public ReactiveProperty<int> F119 = new(0);
        [Replicated] public ReactiveProperty<int> F120 = new(0);
        [Replicated] public ReactiveProperty<int> F121 = new(0);
        [Replicated] public ReactiveProperty<int> F122 = new(0);
        [Replicated] public ReactiveProperty<int> F123 = new(0);
        [Replicated] public ReactiveProperty<int> F124 = new(0);
        [Replicated] public ReactiveProperty<int> F125 = new(0);
        [Replicated] public ReactiveProperty<int> F126 = new(0);
        [Replicated] public ReactiveProperty<int> F127 = new(0);
        [Replicated] public ReactiveProperty<int> F128 = new(0);
        [Replicated] public ReactiveProperty<int> F129 = new(0);
        [Replicated] public ReactiveProperty<int> F130 = new(0);
        [Replicated] public ReactiveProperty<int> F131 = new(0);
        [Replicated] public ReactiveProperty<int> F132 = new(0);
        [Replicated] public ReactiveProperty<int> F133 = new(0);
        [Replicated] public ReactiveProperty<int> F134 = new(0);
        [Replicated] public ReactiveProperty<int> F135 = new(0);
        [Replicated] public ReactiveProperty<int> F136 = new(0);
        [Replicated] public ReactiveProperty<int> F137 = new(0);
        [Replicated] public ReactiveProperty<int> F138 = new(0);
        [Replicated] public ReactiveProperty<int> F139 = new(0);
        [Replicated] public ReactiveProperty<int> F140 = new(0);
        [Replicated] public ReactiveProperty<int> F141 = new(0);
        [Replicated] public ReactiveProperty<int> F142 = new(0);
        [Replicated] public ReactiveProperty<int> F143 = new(0);
        [Replicated] public ReactiveProperty<int> F144 = new(0);
        [Replicated] public ReactiveProperty<int> F145 = new(0);
        [Replicated] public ReactiveProperty<int> F146 = new(0);
        [Replicated] public ReactiveProperty<int> F147 = new(0);
        [Replicated] public ReactiveProperty<int> F148 = new(0);
        [Replicated] public ReactiveProperty<int> F149 = new(0);
        [Replicated] public ReactiveProperty<int> F150 = new(0);
        [Replicated] public ReactiveProperty<int> F151 = new(0);
        [Replicated] public ReactiveProperty<int> F152 = new(0);
        [Replicated] public ReactiveProperty<int> F153 = new(0);
        [Replicated] public ReactiveProperty<int> F154 = new(0);
        [Replicated] public ReactiveProperty<int> F155 = new(0);
        [Replicated] public ReactiveProperty<int> F156 = new(0);
        [Replicated] public ReactiveProperty<int> F157 = new(0);
        [Replicated] public ReactiveProperty<int> F158 = new(0);
        [Replicated] public ReactiveProperty<int> F159 = new(0);
        [Replicated] public ReactiveProperty<int> F160 = new(0);
        [Replicated] public ReactiveProperty<int> F161 = new(0);
        [Replicated] public ReactiveProperty<int> F162 = new(0);
        [Replicated] public ReactiveProperty<int> F163 = new(0);
        [Replicated] public ReactiveProperty<int> F164 = new(0);
        [Replicated] public ReactiveProperty<int> F165 = new(0);
        [Replicated] public ReactiveProperty<int> F166 = new(0);
        [Replicated] public ReactiveProperty<int> F167 = new(0);
        [Replicated] public ReactiveProperty<int> F168 = new(0);
        [Replicated] public ReactiveProperty<int> F169 = new(0);
        [Replicated] public ReactiveProperty<int> F170 = new(0);
        [Replicated] public ReactiveProperty<int> F171 = new(0);
        [Replicated] public ReactiveProperty<int> F172 = new(0);
        [Replicated] public ReactiveProperty<int> F173 = new(0);
        [Replicated] public ReactiveProperty<int> F174 = new(0);
        [Replicated] public ReactiveProperty<int> F175 = new(0);
        [Replicated] public ReactiveProperty<int> F176 = new(0);
        [Replicated] public ReactiveProperty<int> F177 = new(0);
        [Replicated] public ReactiveProperty<int> F178 = new(0);
        [Replicated] public ReactiveProperty<int> F179 = new(0);
        [Replicated] public ReactiveProperty<int> F180 = new(0);
        [Replicated] public ReactiveProperty<int> F181 = new(0);
        [Replicated] public ReactiveProperty<int> F182 = new(0);
        [Replicated] public ReactiveProperty<int> F183 = new(0);
        [Replicated] public ReactiveProperty<int> F184 = new(0);
        [Replicated] public ReactiveProperty<int> F185 = new(0);
        [Replicated] public ReactiveProperty<int> F186 = new(0);
        [Replicated] public ReactiveProperty<int> F187 = new(0);
        [Replicated] public ReactiveProperty<int> F188 = new(0);
        [Replicated] public ReactiveProperty<int> F189 = new(0);
        [Replicated] public ReactiveProperty<int> F190 = new(0);
        [Replicated] public ReactiveProperty<int> F191 = new(0);
        [Replicated] public ReactiveProperty<int> F192 = new(0);
        [Replicated] public ReactiveProperty<int> F193 = new(0);
        [Replicated] public ReactiveProperty<int> F194 = new(0);
        [Replicated] public ReactiveProperty<int> F195 = new(0);
        [Replicated] public ReactiveProperty<int> F196 = new(0);
        [Replicated] public ReactiveProperty<int> F197 = new(0);
        [Replicated] public ReactiveProperty<int> F198 = new(0);
        [Replicated] public ReactiveProperty<int> F199 = new(0);
        [Replicated] public ReactiveProperty<int> F200 = new(0);
        [Replicated] public ReactiveProperty<int> F201 = new(0);
        [Replicated] public ReactiveProperty<int> F202 = new(0);
        [Replicated] public ReactiveProperty<int> F203 = new(0);
        [Replicated] public ReactiveProperty<int> F204 = new(0);
        [Replicated] public ReactiveProperty<int> F205 = new(0);
        [Replicated] public ReactiveProperty<int> F206 = new(0);
        [Replicated] public ReactiveProperty<int> F207 = new(0);
        [Replicated] public ReactiveProperty<int> F208 = new(0);
        [Replicated] public ReactiveProperty<int> F209 = new(0);
        [Replicated] public ReactiveProperty<int> F210 = new(0);
        [Replicated] public ReactiveProperty<int> F211 = new(0);
        [Replicated] public ReactiveProperty<int> F212 = new(0);
        [Replicated] public ReactiveProperty<int> F213 = new(0);
        [Replicated] public ReactiveProperty<int> F214 = new(0);
        [Replicated] public ReactiveProperty<int> F215 = new(0);
        [Replicated] public ReactiveProperty<int> F216 = new(0);
        [Replicated] public ReactiveProperty<int> F217 = new(0);
        [Replicated] public ReactiveProperty<int> F218 = new(0);
        [Replicated] public ReactiveProperty<int> F219 = new(0);
        [Replicated] public ReactiveProperty<int> F220 = new(0);
        [Replicated] public ReactiveProperty<int> F221 = new(0);
        [Replicated] public ReactiveProperty<int> F222 = new(0);
        [Replicated] public ReactiveProperty<int> F223 = new(0);
        [Replicated] public ReactiveProperty<int> F224 = new(0);
        [Replicated] public ReactiveProperty<int> F225 = new(0);
        [Replicated] public ReactiveProperty<int> F226 = new(0);
        [Replicated] public ReactiveProperty<int> F227 = new(0);
        [Replicated] public ReactiveProperty<int> F228 = new(0);
        [Replicated] public ReactiveProperty<int> F229 = new(0);
        [Replicated] public ReactiveProperty<int> F230 = new(0);
        [Replicated] public ReactiveProperty<int> F231 = new(0);
        [Replicated] public ReactiveProperty<int> F232 = new(0);
        [Replicated] public ReactiveProperty<int> F233 = new(0);
        [Replicated] public ReactiveProperty<int> F234 = new(0);
        [Replicated] public ReactiveProperty<int> F235 = new(0);
        [Replicated] public ReactiveProperty<int> F236 = new(0);
        [Replicated] public ReactiveProperty<int> F237 = new(0);
        [Replicated] public ReactiveProperty<int> F238 = new(0);
        [Replicated] public ReactiveProperty<int> F239 = new(0);
        [Replicated] public ReactiveProperty<int> F240 = new(0);
        [Replicated] public ReactiveProperty<int> F241 = new(0);
        [Replicated] public ReactiveProperty<int> F242 = new(0);
        [Replicated] public ReactiveProperty<int> F243 = new(0);
        [Replicated] public ReactiveProperty<int> F244 = new(0);
        [Replicated] public ReactiveProperty<int> F245 = new(0);
        [Replicated] public ReactiveProperty<int> F246 = new(0);
        [Replicated] public ReactiveProperty<int> F247 = new(0);
        [Replicated] public ReactiveProperty<int> F248 = new(0);
        [Replicated] public ReactiveProperty<int> F249 = new(0);
        [Replicated] public ReactiveProperty<int> F250 = new(0);
        [Replicated] public ReactiveProperty<int> F251 = new(0);
        [Replicated] public ReactiveProperty<int> F252 = new(0);
        [Replicated] public ReactiveProperty<int> F253 = new(0);
        [Replicated] public ReactiveProperty<int> F254 = new(0);
        [Replicated] public ReactiveProperty<int> F255 = new(0);
        [Replicated] public ReactiveProperty<int> F256 = new(0);
    }

    /// <summary>Forces <see cref="GiantStateAspect"/> creation in Awake.</summary>
    public sealed class GiantStateAspectRegistrar : MonoBehaviour, IEntityComponent
    {
        private void Awake()
        {
            GetComponentInParent<MonoEntity>().Require<GiantStateAspect>();
        }
    }
}
