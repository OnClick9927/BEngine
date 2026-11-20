#include <windows.h>
#include <icm.h>
#include <stdio.h>
#include <fstream>
#include <vector>
#include <filesystem>
#include <stdexcept>
static_assert(sizeof(COLOR) == 16, "COLOR x64 ABI");
static HPROFILE open(const std::filesystem::path& path){
 std::ifstream f(path,std::ios::binary); std::vector<char>b((std::istreambuf_iterator<char>(f)),{});
 PROFILE p{PROFILE_MEMBUFFER,b.data(),static_cast<DWORD>(b.size())};
 auto handle=OpenColorProfileW(&p,PROFILE_READ,FILE_SHARE_READ,OPEN_EXISTING); if(!handle)throw std::runtime_error("OpenColorProfile");return handle;
}
static void convert(const std::filesystem::path& input,const std::filesystem::path& output,const std::vector<COLOR>& samples,COLORTYPE type){
 auto source=open(input);DWORD size=0;GetStandardColorSpaceProfileW(nullptr,LCS_sRGB,nullptr,&size);
 std::vector<wchar_t> name(size/sizeof(wchar_t));if(!GetStandardColorSpaceProfileW(nullptr,LCS_sRGB,name.data(),&size))throw std::runtime_error("standard profile");
 auto destination=open(name.data());HPROFILE profiles[]{source,destination};DWORD intents[]{INTENT_RELATIVE_COLORIMETRIC};
 auto transform=CreateMultiProfileTransform(profiles,2,intents,1,0,0);if(!transform)throw std::runtime_error("create transform");
 std::vector<COLOR> result(samples.size());if(!TranslateColors(transform,const_cast<COLOR*>(samples.data()),static_cast<DWORD>(samples.size()),type,result.data(),COLOR_RGB))throw std::runtime_error("translate colors");
 std::ofstream file(output,std::ios::binary);for(auto &c:result){file.write(reinterpret_cast<char*>(&c.rgb.red),2);file.write(reinterpret_cast<char*>(&c.rgb.green),2);file.write(reinterpret_cast<char*>(&c.rgb.blue),2);}
 DeleteColorTransform(transform);CloseColorProfile(destination);CloseColorProfile(source);
}
int wmain(int argc,wchar_t**argv){
 if(argc!=4)return 1;
 try{
  const std::filesystem::path profile=argv[1],folder=argv[2];std::filesystem::create_directories(folder);
  std::vector<COLOR> rgb(4097),direct(513);
  for(size_t i=0;i<rgb.size();i++){rgb[i].rgb.red=static_cast<WORD>(((i*37+13)%256)*257);rgb[i].rgb.green=static_cast<WORD>(((i*37+74)%256)*257);rgb[i].rgb.blue=static_cast<WORD>(((i*37+135)%256)*257);}
  for(size_t i=0;i<direct.size();i++){direct[i].rgb.red=static_cast<WORD>(i*127);direct[i].rgb.green=static_cast<WORD>(i*89);direct[i].rgb.blue=static_cast<WORD>(i*31);}
  convert(profile,folder/L"Rgb.bin",rgb,COLOR_RGB);convert(profile,folder/L"Direct.bin",direct,COLOR_RGB);
  std::vector<COLOR> gray(257);for(size_t i=0;i<gray.size();i++)gray[i].gray.gray=static_cast<WORD>(i*65535/256);convert(argv[3],folder/L"Gray.bin",gray,COLOR_GRAY);
  printf("COLOR=%zu RGB=%u GRAY=%u NativeSamples=%zu\n",sizeof(COLOR),COLOR_RGB,COLOR_GRAY,rgb.size()+direct.size());return 0;
 }catch(const std::exception& e){printf("%s error=%lu\n",e.what(),GetLastError());return 2;}
}
