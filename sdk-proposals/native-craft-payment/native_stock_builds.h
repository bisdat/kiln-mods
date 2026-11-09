// SPDX-License-Identifier: MIT
// Exact original stock selector executable identities. Each set of RVAs is
// independently established from the appropriate game's executable, never
// transferred between the dedicated server and a hosted single-player client.
// The client host addresses below were verified from the user's own
// 26430625 read-only callmap; see PR #7, 9 Nov 2026.
#pragma once
#include <cstdint>
#include <cstring>

namespace cbpay::stock::build {
enum class Side : uint32_t { Dedicated=0, ClientHosted=1 };
struct Profile {
    Side side;
    const char* sha256;
    uint32_t steam_build;
    uint32_t selector_rva;
    uint32_t payer_rva;
    uint32_t call1_rva;
    uint32_t call2_rva;
    uint32_t recipe_apply_rva;
};

inline constexpr Profile kDedicated{
    Side::Dedicated,
    "f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2",
    26430253u,0x2081c0u,0x199c80u,0x199d8eu,0x199fa2u,0x19a1d0u
};
inline constexpr Profile kClientHosted{
    Side::ClientHosted,
    "5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77",
    26430625u,0x46fa10u,0x3f89e0u,0x3f8aeeu,0x3f8d02u,0x3f8f30u
};

inline const Profile* exact(Side side, const char* hash) {
    if (!hash || std::strlen(hash)!=64u) return nullptr;
    const Profile& p=side==Side::Dedicated?kDedicated:kClientHosted;
    return std::strcmp(hash,p.sha256)==0?&p:nullptr;
}
inline uint32_t first_return(const Profile& p) {
    return p.call1_rva+5u; // return of one original direct E8 call
}
inline uint32_t second_return(const Profile& p) {
    return p.call2_rva+5u;
}
inline bool original_return(const Profile& p, uint32_t rva) {
    return rva==first_return(p) || rva==second_return(p);
}
} // namespace cbpay::stock::build
