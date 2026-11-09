// SPDX-License-Identifier: MIT
#include "native_stock_builds.h"
#include <cassert>
#include <cstdio>
using namespace cbpay::stock::build;
int main() {
    unsigned n=0;
    auto test=[&](const char* s, bool ok) {
        assert(ok); std::printf("PASS %02u %s\n",++n,s);
    };
    const Profile* s=exact(Side::Dedicated,kDedicated.sha256);
    const Profile* c=exact(Side::ClientHosted,kClientHosted.sha256);
    test("pinned dedicated-server hash identified", s==&kDedicated);
    test("pinned client host game hash identified", c==&kClientHosted);
    test("user client selector at observed 0x46fa10", c&&c->selector_rva==0x46fa10u);
    test("user client payer at observed 0x3f89e0",c&&c->payer_rva==0x3f89e0u);
    test("user client original calls matched",c&&c->call1_rva==0x3f8aeeu&&c->call2_rva==0x3f8d02u);
    test("caller return addresses point AFTER 5-byte E8",c&&first_return(*c)==0x3f8af3u&&second_return(*c)==0x3f8d07u);
    test("server caller return addresses retained",s&&first_return(*s)==0x199d93u&&second_return(*s)==0x199fa7u);
    test("both original client call branches only", c&&original_return(*c,0x3f8af3u)&&original_return(*c,0x3f8d07u)&&!original_return(*c,0x3f8aeeu)&&!original_return(*c,0));
    test("client SHA cannot masquerade as dedicated server",exact(Side::Dedicated,kClientHosted.sha256)==nullptr);
    test("dedicated SHA cannot masquerade as client host",exact(Side::ClientHosted,kDedicated.sha256)==nullptr);
    test("unknown, short, or null hashes fail closed",exact(Side::ClientHosted,"0000000000000000000000000000000000000000000000000000000000000000")==nullptr&&exact(Side::ClientHosted,"abc")==nullptr&&exact(Side::ClientHosted,nullptr)==nullptr);
    test("both game revisions bind distinct selector RVAs", s&&c&&s->selector_rva!=c->selector_rva);
    std::printf("PASS %u exact-build host-or-server address contract cases. No live hook executed.\n",n);
}
