use strict;
use warnings;
use File::Spec;
use File::Copy;
use File::Path qw(make_path rmtree);

my @roots = ('Data', 'Textures');
#my $destination = '..\\GardenConquestDistro\\';
my $destination = 'C:\\Users\\Zach\\AppData\\Roaming\\SpaceEngineers\\Mods\\GardenConquestDistro\\';

foreach my $r (@roots) {
	my $remove = File::Spec->catfile($destination, $r);
	if(-e $remove) {
		print("Cleaning root $remove\n");
		rmtree($remove);
	}
	
	processDir($r);
}

sub processDir {
	my ($path) = @_;

	print("Processing directory $path\n");
	opendir(DIR, $path) or die $!;

	my @files = readdir(DIR);
	closedir(DIR);

	# Smoosh all the namespace directories down
	my $destPath = "";
	if($path =~ m!Data\\Scripts\\GardenConquest\\!) {
		$destPath = File::Spec->catfile($destination, 'Data\\Scripts\\GardenConquest');
	} else {
		$destPath = File::Spec->catfile($destination, $path);
	}

	unless(-e $destPath) {
		make_path($destPath);
	}

	foreach my $entry (@files) {
		next if($entry eq '.' || $entry eq '..');
	 	next if($entry eq 'bin' || $entry eq 'obj');

	 	my $full = File::Spec->catfile($path, $entry);

		if(-d $full) {
			processDir($full);
		} elsif($entry =~ /\.(cs|sbc|dds)$/) {
			my $dest = File::Spec->catfile($destPath, $entry);
			 
			print("Copying file $entry -> $dest\n");
			copy($full, $dest) or die "Could not copy: $!";
		}
	}
}
